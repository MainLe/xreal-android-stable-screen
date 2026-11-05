using Unity.Collections;

/// <summary>Copy valid RGBA pixels only; Android row padding is not part of the Unity texture.</summary>
public static class MirrorFrameCopy
{
    public static bool Copy(NativeArray<byte> source,int byteCount,int width,int height,int rowStride,int pixelStride,NativeArray<byte> destination)
    {
        if(!source.IsCreated||!destination.IsCreated||width<=0||height<=0||pixelStride!=4)return false;
        long rowBytes=(long)width*4,required=(long)(height-1)*rowStride+rowBytes,destinationBytes=rowBytes*height;
        if(rowStride<rowBytes||required>byteCount||byteCount>source.Length||destinationBytes!=destination.Length||required>int.MaxValue)return false;
        if(rowStride==rowBytes)NativeArray<byte>.Copy(source,0,destination,0,(int)destinationBytes);
        else for(int row=0;row<height;row++)NativeArray<byte>.Copy(source,row*rowStride,destination,row*(int)rowBytes,(int)rowBytes);
        return true;
    }
}
