using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>Bounded native registration retries; pending is an AR notification flag, not tracking failure.</summary>
public sealed class PlaneAnchorCreation
{
    public enum Decision { Wait,Attempt,Ready,TimedOut }
    public const float TimeoutSeconds=15,RetryInterval=.75f;
    public const int MaximumAttempts=12;
    public bool Active { get; private set; }
    public int Attempts { get; private set; }
    public float StartedAt { get; private set; }
    float nextAttempt;
    public void Begin(float now){Active=true;Attempts=0;StartedAt=nextAttempt=now;}
    public void Cancel(){Active=false;}
    public Decision Step(float now,bool serviceReady,bool hasAnchor,bool validId,bool pending,TrackingState tracking)
    {
        if(!Active)return Decision.Wait;
        // A real native ID and Tracking result are sufficient even before the
        // subsystem emits its separate added/updated notification (pending=true).
        if(hasAnchor&&validId&&tracking==TrackingState.Tracking){Active=false;return Decision.Ready;}
        if(now-StartedAt>=TimeoutSeconds){Active=false;return Decision.TimedOut;}
        if(hasAnchor||!serviceReady||now<nextAttempt||Attempts>=MaximumAttempts)return Decision.Wait;
        Attempts++;nextAttempt=now+RetryInterval;return Decision.Attempt;
    }
}

public static class PlaneNativeAnchorFactory
{
    public static ARAnchor TryCreate(ARAnchorManager manager,Pose pose)
    {
        // This project's AR Foundation 5.1.5 exposes this synchronous factory.
        // It returns null on a transient native refusal, and parents a successful
        // anchor under TrackablesParent without an unregistered component's
        // automatic per-frame retries. Keep retry cadence in PlaneAnchorCreation.
#pragma warning disable 618
        return manager.AddAnchor(pose);
#pragma warning restore 618
    }
}
