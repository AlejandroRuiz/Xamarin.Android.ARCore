using System;
using Com.Google.AR.Core;

namespace Xamarin.Android.ARCore.Test.Rendering
{
    public class PlaneAttachment
    {
		private Plane mPlane;
		private Anchor mAnchor;

		// Allocate temporary storage to avoid multiple allocations per frame.
		private float[] mPoseTranslation = new float[3];
		private float[] mPoseRotation = new float[4];

		public PlaneAttachment(Plane plane, Anchor anchor)
		{
			mPlane = plane;
			mAnchor = anchor;
		}

        public bool IsTracking()
		{
			return /*true if*/
                mPlane.GetTrackingState() == Plane.TrackingState.Tracking &&
				mAnchor.GetTrackingState() == Anchor.TrackingState.Tracking;
		}

		public Pose GetPose()
		{
            Pose pose = mAnchor.Pose;
			pose.GetTranslation(mPoseTranslation, 0);
			pose.GetRotationQuaternion(mPoseRotation, 0);
            mPoseTranslation[1] = mPlane.CenterPose.Ty();
			return new Pose(mPoseTranslation, mPoseRotation);
		}

		public Anchor GetAnchor()
		{
			return mAnchor;
		}
    }
}
