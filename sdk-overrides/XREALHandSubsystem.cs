#if UNITY_ANDROID && XR_HANDS
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.ProviderImplementation;

namespace Unity.XR.XREAL
{
    /// <summary>
    /// The XREAL implementation of the
    /// [XRHandSubsystem](xref:UnityEngine.XR.Hands.XRHandSubsystem).
    /// </summary>
    [Preserve]
    public sealed class XREALHandSubSystem : XRHandSubsystem
    {
#if XREAL_HAND_RUNTIME_AUDIT
        // Enabled only by the independent runtime-audit build. Proprietary native libraries are unchanged.
        public static class PollingDiagnostics
        {
            public static bool ReuseBeforeRender;
            public static long Calls,NoTrackedResults,NativeReadReturnedFalse,BeforeRenderCalls,BeforeRenderReuses;
            public static double TotalMs,WorstMs;
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            XRHandSubsystemDescriptor.Register(new XRHandSubsystemDescriptor.Cinfo
            {
                id = "XREAL Hands",
                providerType = typeof(XREALXRHandProvider),
                subsystemTypeOverride = typeof(XREALHandSubSystem),
            });
        }

        XRHandProviderUtility.SubsystemUpdater m_Updater;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Updater = new XRHandProviderUtility.SubsystemUpdater(this);
        }

        protected override void OnStart()
        {
            m_Updater.Start();
            base.OnStart();
        }

        protected override void OnStop()
        {
            m_Updater.Stop();
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            m_Updater.Destroy();
            m_Updater = null;
            base.OnDestroy();
        }

        class XREALXRHandProvider : XRHandSubsystemProvider
        {
            public override void GetHandLayout(NativeArray<bool> handJointsInLayout)
            {
                handJointsInLayout[XRHandJointID.Wrist.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.Palm.ToIndex()] = true;

                handJointsInLayout[XRHandJointID.ThumbMetacarpal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.ThumbProximal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.ThumbDistal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.ThumbTip.ToIndex()] = true;

                handJointsInLayout[XRHandJointID.IndexMetacarpal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.IndexProximal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.IndexIntermediate.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.IndexDistal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.IndexTip.ToIndex()] = true;

                handJointsInLayout[XRHandJointID.MiddleMetacarpal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.MiddleProximal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.MiddleIntermediate.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.MiddleDistal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.MiddleTip.ToIndex()] = true;

                handJointsInLayout[XRHandJointID.RingMetacarpal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.RingProximal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.RingIntermediate.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.RingDistal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.RingTip.ToIndex()] = true;

                handJointsInLayout[XRHandJointID.LittleMetacarpal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.LittleProximal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.LittleIntermediate.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.LittleDistal.ToIndex()] = true;
                handJointsInLayout[XRHandJointID.LittleTip.ToIndex()] = true;
            }

            public override void Start()
            {
            }

            public override void Stop()
            {
            }

            public override void Destroy()
            {
            }

            HandJointsPose m_HandJointsPose = new HandJointsPose();
#if XREAL_HAND_RUNTIME_AUDIT
            int lastPolledFrame=-1;
            UpdateSuccessFlags cachedFlags;
            Pose cachedLeftRoot,cachedRightRoot;
#endif
            public override UpdateSuccessFlags TryUpdateHands(UpdateType updateType, ref Pose leftHandRootPose, NativeArray<XRHandJoint> leftHandJoints, ref Pose rightHandRootPose, NativeArray<XRHandJoint> rightHandJoints)
            {
#if XREAL_HAND_RUNTIME_AUDIT
                if(XREALPlugin.GetInputSource()!=InputSource.Hands){lastPolledFrame=-1;return UpdateSuccessFlags.None;}
                if(PollingDiagnostics.ReuseBeforeRender&&updateType==UpdateType.BeforeRender&&lastPolledFrame==Time.frameCount)
                {
                    // XR Hands 1.3 passes the same persistent joint arrays at both update phases.
                    leftHandRootPose=cachedLeftRoot;rightHandRootPose=cachedRightRoot;PollingDiagnostics.BeforeRenderReuses++;return cachedFlags;
                }
                long started=System.Diagnostics.Stopwatch.GetTimestamp();
                UpdateSuccessFlags result=UpdateSuccessFlags.None;
                try
                {
                    result=ReadHands(ref leftHandRootPose,leftHandJoints,ref rightHandRootPose,rightHandJoints);
                    cachedLeftRoot=leftHandRootPose;cachedRightRoot=rightHandRootPose;cachedFlags=result;lastPolledFrame=Time.frameCount;
                    return result;
                }
                finally
                {
                    double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    PollingDiagnostics.Calls++;if(updateType==UpdateType.BeforeRender)PollingDiagnostics.BeforeRenderCalls++;
                    if(result==UpdateSuccessFlags.None)PollingDiagnostics.NoTrackedResults++;
                    PollingDiagnostics.TotalMs+=ms;PollingDiagnostics.WorstMs=System.Math.Max(PollingDiagnostics.WorstMs,ms);
                }
#else
                return ReadHands(ref leftHandRootPose,leftHandJoints,ref rightHandRootPose,rightHandJoints);
#endif
            }
            UpdateSuccessFlags ReadHands(ref Pose leftHandRootPose, NativeArray<XRHandJoint> leftHandJoints, ref Pose rightHandRootPose, NativeArray<XRHandJoint> rightHandJoints)
            {
                UpdateSuccessFlags ret = UpdateSuccessFlags.None;
                if (!XREALPlugin.UpdateHandPose())
                {
#if XREAL_HAND_RUNTIME_AUDIT
                    PollingDiagnostics.NativeReadReturnedFalse++;
#endif
                    return ret;
                }
                if (XREALPlugin.GetHandJointsPose(HandType.LeftHand, ref m_HandJointsPose))
                {
                    if (m_HandJointsPose.isTracked)
                    {
                        leftHandRootPose = m_HandJointsPose.jointsPose[0];
                        for (int i = 0; i < m_HandJointsPose.jointsPose.Length; i++)
                        {
                            leftHandJoints[i] = XRHandProviderUtility.CreateJoint(Handedness.Left,
                                XRHandJointTrackingState.Pose,
                                XRHandJointIDUtility.FromIndex(i),
                                m_HandJointsPose.jointsPose[i]);
                        }
                        ret |= UpdateSuccessFlags.LeftHandRootPose | UpdateSuccessFlags.LeftHandJoints;
                    }
                }
                if (XREALPlugin.GetHandJointsPose(HandType.RightHand, ref m_HandJointsPose))
                {
                    if (m_HandJointsPose.isTracked)
                    {
                        rightHandRootPose = m_HandJointsPose.jointsPose[0];
                        for (int i = 0; i < m_HandJointsPose.jointsPose.Length; i++)
                        {
                            rightHandJoints[i] = XRHandProviderUtility.CreateJoint(Handedness.Right,
                                XRHandJointTrackingState.Pose,
                                XRHandJointIDUtility.FromIndex(i),
                                m_HandJointsPose.jointsPose[i]);
                        }
                        ret |= UpdateSuccessFlags.RightHandRootPose | UpdateSuccessFlags.RightHandJoints;
                    }
                }
                return ret;
            }
        }
    }

    internal enum HandType
    {
        LeftHand = 0,
        RightHand = 1,
    }

    internal struct HandJointsPose
    {
        public bool isTracked;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)XRHandJointID.EndMarker - 1)]
        public Pose[] jointsPose;
    }

    public static partial class XREALPlugin
    {
        [DllImport(LibName)]
        internal static extern bool UpdateHandPose();

        [DllImport(LibName)]
        internal static extern bool GetHandJointsPose(HandType handType, ref HandJointsPose handJoints);
    }
}
#endif
