import dev.xreal.stablescreen.CaptureSizing;
import dev.xreal.stablescreen.FrameMailbox;
import java.nio.ByteBuffer;
import java.util.concurrent.atomic.AtomicReference;

public final class FrameMailboxTest {
    static void check(boolean value,String name){if(!value)throw new AssertionError(name);}
    public static void main(String[] args)throws Exception{
        int[] portrait=CaptureSizing.fit(1200,2670,1600),landscape=CaptureSizing.fit(2670,1200,1600);
        check(portrait[1]==1600&&landscape[0]==1600,"long edge");
        check(portrait[0]==landscape[1],"rotation symmetry");
        check(Math.abs((double)portrait[0]/portrait[1]-1200.0/2670)<.002,"aspect preserved");
        for(int profile=0;profile<3;profile++)for(int i=0;i<40;i++){
            int edge=CaptureSizing.profileEdge(profile);int[] region=CaptureSizing.squareCrop(i%2==0?1200:2670,i%2==0?2670:1200,edge);
            check(region[0]>=0&&region[1]>=0&&region[0]+region[2]<=edge&&region[1]+region[3]<=edge,"fixed-surface crop bounds after rotations");
            check(region[2]==edge||region[3]==edge,"full long edge retained");
        }
        FrameMailbox box=new FrameMailbox();box.reset(true);
        ByteBuffer padded=ByteBuffer.allocateDirect(28);for(int i=0;i<28;i++)padded.put(i,(byte)i);
        check(box.publish(padded,3,2,16,4,10,20),"padded buffer accepted");
        FrameMailbox.Frame held=box.acquire();check(held.byteCount==28&&held.buffer.get(16)==16,"pixel layout");
        box.reset(false);check(!box.publish(padded,3,2,16,4,11,21),"stopped capture rejects frames");
        box.reset(true);for(int i=0;i<100;i++)box.publish(padded,3,2,16,4,i,i);
        check(held.buffer.get(16)==16,"outstanding frame survives reset and replacement");held.close();held.close();
        FrameMailbox.Frame last=box.acquire();check(last!=null,"latest frame");last.close();
        check(box.stats()[3]==0&&box.stats()[5]==3,"all three slots returned after repeated close");
        boolean rejected=false;try{box.publish(ByteBuffer.allocateDirect(27),3,2,16,4,0,0);}catch(IllegalArgumentException e){rejected=true;}check(rejected,"truncated final row rejected");
        rejected=false;try{box.publish(padded,3,2,8,4,0,0);}catch(IllegalArgumentException e){rejected=true;}check(rejected,"short stride rejected");
        ByteBuffer square=ByteBuffer.allocateDirect(8*8*4);for(int i=0;i<square.capacity();i++)square.put(i,(byte)i);
        check(box.publishCropped(square,8,8,32,4,2,1,4,6,1,2),"portrait cropped publish");
        FrameMailbox.Frame crop=box.acquire();check(crop.width==4&&crop.height==6&&crop.rowStride==16&&crop.byteCount==96,"packed crop metadata");
        for(int row=0;row<6;row++)for(int col=0;col<16;col++)check(crop.buffer.get(row*16+col)==square.get((row+1)*32+8+col),"crop removes bars without leaking padding");
        box.reset(true);check(crop.buffer.get(0)==square.get(40),"cropped lease survives rotation reset");crop.close();
        for(int i=0;i<120;i++){boolean wide=(i%2)==0;check(box.publishCropped(square,8,8,32,4,wide?0:2,wide?2:0,wide?8:4,wide?4:8,i,i),"rotation stress publish");FrameMailbox.Frame f=box.acquire();check(f.byteCount==128,"rotation packed dimensions");f.close();}
        AtomicReference<Throwable> failure=new AtomicReference<>();
        Thread producer=new Thread(()->{
            try{ByteBuffer data=ByteBuffer.allocateDirect(64);for(int n=0;n<10000;n++){for(int i=0;i<64;i++)data.put(i,(byte)n);box.publish(data,4,4,16,4,n,n);}}
            catch(Throwable e){failure.set(e);}
        });
        Thread consumer=new Thread(()->{
            try{for(int n=0;n<10000;n++){FrameMailbox.Frame frame=box.acquire();if(frame==null){Thread.yield();continue;}try{byte first=frame.buffer.get(0);for(int i=1;i<64;i++)check(frame.buffer.get(i)==first,"leased frame torn by producer");}finally{frame.close();}}}
            catch(Throwable e){failure.set(e);}
        });
        producer.start();consumer.start();producer.join();consumer.join();if(failure.get()!=null)throw new AssertionError(failure.get());
        box.reset(false);check(box.stats()[3]==0&&box.stats()[5]==3,"no buffers leaked after concurrent stop");
        System.out.println("PASSED: fixed-square crops, 120 rotation transitions, packing, padding, truncated input, stop/reset lifetime, 10000-frame concurrent lease stress");
    }
}
