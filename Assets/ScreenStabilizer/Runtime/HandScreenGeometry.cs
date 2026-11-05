using UnityEngine;

/// <summary>Screen-normal projection in world metres, independent of finger or head direction.</summary>
public static class HandScreenGeometry
{
    public static Vector3 Project(Pose screen,Vector3 indexTip)=>Quaternion.Inverse(screen.rotation)*(indexTip-screen.position);
    public static Vector2 Uv(Vector3 localMetres,Vector2 size)=>new Vector2(localMetres.x/size.x+.5f,localMetres.y/size.y+.5f);
    // Always smaller than the reference disc, so its fixed outer edge remains visible.
    public static float DistanceRatio(float normalMetres)=>Mathf.Lerp(.22f,.82f,Mathf.Clamp01(Mathf.Abs(normalMetres)/.20f));
}
