package dev.xreal.stablescreen;

import java.nio.ByteBuffer;
import java.util.ArrayDeque;

/** Bounded latest-frame mailbox. Leased memory never belongs to an ImageReader or a recycled frame. */
public final class FrameMailbox {
    public static final class Frame implements AutoCloseable {
        public ByteBuffer buffer;
        public int width,height,rowStride,pixelStride,byteCount,generation;
        public long sequence,capturedAtElapsedMs,sourceTimestampNs;
        public float ageAtAcquireMs;
        private final FrameMailbox owner;
        private boolean leased;
        private Frame(FrameMailbox owner){this.owner=owner;}
        @Override public void close(){owner.release(this);}
    }
    private final ArrayDeque<Frame> free=new ArrayDeque<>();
    private Frame latest;
    private boolean accepting;
    private int generation,leased;
    private long sequence,published,replaced,dropped;
    public FrameMailbox(){for(int i=0;i<3;i++)free.add(new Frame(this));}
    public synchronized void reset(boolean enable){
        accepting=enable;generation++;
        if(latest!=null){free.add(latest);latest=null;}
        published=0;replaced=0;dropped=0;
    }
    public synchronized boolean publish(ByteBuffer source,int width,int height,int rowStride,int pixelStride,long elapsedMs,long sourceNs){
        if(!accepting)return false;
        if(source==null||width<=0||height<=0||width>8192||height>8192||pixelStride!=4||rowStride<(long)width*4)throw new IllegalArgumentException("Unsupported RGBA frame layout");
        long needed=(long)(height-1)*rowStride+(long)width*4;
        if(needed>Integer.MAX_VALUE||source.remaining()<needed)throw new IllegalArgumentException("Truncated frame buffer");
        Frame frame=free.poll();
        if(frame==null&&latest!=null){frame=latest;latest=null;replaced++;}
        if(frame==null){dropped++;return false;}
        try{
            int size=(int)needed;
            if(frame.buffer==null||frame.buffer.capacity()<size)frame.buffer=ByteBuffer.allocateDirect(size);
            ByteBuffer view=source.duplicate();view.limit(view.position()+size);
            frame.buffer.clear();frame.buffer.put(view);frame.buffer.flip();
            frame.width=width;frame.height=height;frame.rowStride=rowStride;frame.pixelStride=pixelStride;frame.byteCount=size;
            frame.capturedAtElapsedMs=elapsedMs;frame.sourceTimestampNs=sourceNs;frame.sequence=++sequence;frame.generation=generation;
            if(latest!=null){free.add(latest);replaced++;}
            latest=frame;published++;return true;
        }catch(RuntimeException|Error e){free.add(frame);throw e;}
    }
    public synchronized Frame acquire(){Frame frame=latest;latest=null;if(frame!=null){frame.leased=true;leased++;}return frame;}
    /** Packs just the centered phone content; Unity can upload one contiguous RGBA block. */
    public synchronized boolean publishCropped(ByteBuffer source,int imageWidth,int imageHeight,int rowStride,int pixelStride,int left,int top,int width,int height,long elapsedMs,long sourceNs){
        if(!accepting)return false;
        if(source==null||pixelStride!=4||width<=0||height<=0||left<0||top<0||left+(long)width>imageWidth||top+(long)height>imageHeight||rowStride<(long)imageWidth*4)throw new IllegalArgumentException("Invalid crop layout");
        long required=(long)(top+height-1)*rowStride+(long)(left+width)*4;
        long packed=(long)width*height*4;
        if(required>source.remaining()||packed>Integer.MAX_VALUE)throw new IllegalArgumentException("Truncated cropped frame");
        Frame frame=free.poll();if(frame==null&&latest!=null){frame=latest;latest=null;replaced++;}
        if(frame==null){dropped++;return false;}
        try{
            int bytes=(int)packed,rowBytes=width*4,start=source.position()+top*rowStride+left*4;
            if(frame.buffer==null||frame.buffer.capacity()<bytes)frame.buffer=ByteBuffer.allocateDirect(bytes);
            frame.buffer.clear();ByteBuffer view=source.duplicate();
            if(rowStride==rowBytes){view.position(start);view.limit(start+bytes);frame.buffer.put(view);}
            else for(int row=0;row<height;row++){int offset=start+row*rowStride;view.limit(offset+rowBytes);view.position(offset);frame.buffer.put(view);}
            frame.buffer.flip();frame.width=width;frame.height=height;frame.rowStride=rowBytes;frame.pixelStride=4;frame.byteCount=bytes;
            frame.capturedAtElapsedMs=elapsedMs;frame.sourceTimestampNs=sourceNs;frame.sequence=++sequence;frame.generation=generation;
            if(latest!=null){free.add(latest);replaced++;}latest=frame;published++;return true;
        }catch(RuntimeException|Error e){free.add(frame);throw e;}
    }
    private synchronized void release(Frame frame){if(!frame.leased)return;frame.leased=false;leased--;free.add(frame);}
    public synchronized long[] stats(){return new long[]{published,replaced,dropped,leased,generation,free.size()};}
}
