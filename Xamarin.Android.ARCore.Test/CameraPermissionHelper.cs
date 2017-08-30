using System;
using Android;
using Android.App;
using Android.Content.PM;
using Android.Support.V4.App;
using Android.Support.V4.Content;

namespace Xamarin.Android.ARCore.Test
{
    public class CameraPermissionHelper
    {
        private const string CAMERA_PERMISSION = Manifest.Permission.Camera;
        private const int CAMERA_PERMISSION_CODE = 0;

		/**
		 * Check to see we have the necessary permissions for this app.
		 */
        public static bool HasCameraPermission(Activity activity)
		{
            return ContextCompat.CheckSelfPermission(activity, CAMERA_PERMISSION) == Permission.Granted;
		}

		/**
		 * Check to see we have the necessary permissions for this app, and ask for them if we don't.
		 */
		public static void RequestCameraPermission(Activity activity)
		{
            ActivityCompat.RequestPermissions(activity, new String[] { CAMERA_PERMISSION }, CAMERA_PERMISSION_CODE);
		}
    }
}
