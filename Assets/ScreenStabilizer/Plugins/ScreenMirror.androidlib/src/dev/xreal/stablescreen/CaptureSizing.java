package dev.xreal.stablescreen;

/** Pure sizing logic shared with offline rotation/aspect tests. */
public final class CaptureSizing {
    private CaptureSizing(){}
    public static int[] fit(int width,int height,int limit){
        if(width<=0||height<=0||width>16384||height>16384||limit<320||limit>4096)throw new IllegalArgumentException("Invalid display size");
        double scale=Math.min(1.0,(double)limit/Math.max(width,height));
        // Even dimensions avoid format restrictions without changing the captured aspect materially.
        return new int[]{Math.max(2,((int)Math.round(width*scale))&~1),Math.max(2,((int)Math.round(height*scale))&~1)};
    }
    /** Android 12L+ centers aspect-preserving content inside a fixed square surface. */
    public static int[] squareCrop(int sourceWidth,int sourceHeight,int edge){
        int[] size=fit(sourceWidth,sourceHeight,edge);
        return new int[]{(edge-size[0])/2,(edge-size[1])/2,size[0],size[1]};
    }
    public static int profileEdge(int profile){return profile==0?960:profile==2?1600:1200;}
    public static int profileFps(int profile){return profile==0?30:60;}
}
