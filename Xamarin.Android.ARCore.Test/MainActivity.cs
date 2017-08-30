﻿using Android.App;
using Android.Widget;
using Android.OS;
using Android.Support.V7.App;
using Android.Opengl;
using Com.Google.AR.Core;
using Xamarin.Android.ARCore.Test.Rendering;
using Android.Views;
using System.Collections.Generic;
using Android.Support.Design.Widget;
using System.Collections.Concurrent;
using Javax.Microedition.Khronos.Opengles;
using System.IO;
using Android.Util;
using Java.Lang;

namespace Xamarin.Android.ARCore.Test
{
    [Activity(Label = "Xamarin.Android.ARCore.Test", MainLauncher = true, Icon = "@mipmap/icon", Theme = "@style/Theme.AppCompat.NoActionBar", 
              ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation | global::Android.Content.PM.ConfigChanges.ScreenSize,
              ScreenOrientation = global::Android.Content.PM.ScreenOrientation.Locked)]
    public class MainActivity : AppCompatActivity, GLSurfaceView.IRenderer, GestureDetector.IOnGestureListener, View.IOnTouchListener
    {
        private const string TAG = nameof(MainActivity);

		// Rendering. The Renderers are created here, and initialized when the GL surface is created.
		private GLSurfaceView mSurfaceView;

		private Com.Google.AR.Core.Config mDefaultConfig;
		private Session mSession;
		private BackgroundRenderer mBackgroundRenderer = new BackgroundRenderer();
		private GestureDetector mGestureDetector;
		private Snackbar mLoadingMessageSnackbar = null;

		private ObjectRenderer mVirtualObject = new ObjectRenderer();
		private ObjectRenderer mVirtualObjectShadow = new ObjectRenderer();
		private PlaneRenderer mPlaneRenderer = new PlaneRenderer();
		private PointCloudRenderer mPointCloud = new PointCloudRenderer();

		// Temporary matrix allocated here to reduce number of allocations for each frame.
		private float[] mAnchorMatrix = new float[16];

        // Tap handling and UI.
        private ConcurrentQueue<MotionEvent> mQueuedSingleTaps = new ConcurrentQueue<MotionEvent>();
        private List<PlaneAttachment> mTouches = new List<PlaneAttachment>();

        public bool OnDown(MotionEvent e)
        {
            return true;
        }

        public bool OnFling(MotionEvent e1, MotionEvent e2, float velocityX, float velocityY)
        {
            return true;
        }

        public void OnLongPress(MotionEvent e)
        {
        }

        public bool OnScroll(MotionEvent e1, MotionEvent e2, float distanceX, float distanceY)
        {
            return true;
        }

        public void OnShowPress(MotionEvent e)
        {
        }

        public bool OnSingleTapUp(MotionEvent e)
        {
            onSingleTap(e);
            return true;
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            return mGestureDetector.OnTouchEvent(e);
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.Main);

            mSurfaceView = (GLSurfaceView)FindViewById(Resource.Id.surfaceview);

            mSession = new Session(this);

            // Create default config, check is supported, create session from that config.
            mDefaultConfig = Com.Google.AR.Core.Config.CreateDefaultConfig();
            if (!mSession.IsSupported(mDefaultConfig))
            {
                Toast.MakeText(this, "This device does not support AR", ToastLength.Long).Show();
                Finish();
                return;
            }


            // Set up tap listener.
            mGestureDetector = new GestureDetector(this, this);

            mSurfaceView.SetOnTouchListener(this);

            // Set up renderer.
            mSurfaceView.PreserveEGLContextOnPause = true;
            mSurfaceView.SetEGLContextClientVersion(2);
            mSurfaceView.SetEGLConfigChooser(8, 8, 8, 8, 16, 0); // Alpha used for plane blending.
            mSurfaceView.SetRenderer(this);
            mSurfaceView.RenderMode = Rendermode.Continuously;
        }

        protected override void OnResume()
        {
            base.OnResume();
			// ARCore requires camera permissions to operate. If we did not yet obtain runtime
			// permission on Android M and above, now is a good time to ask the user for it.
			if (CameraPermissionHelper.HasCameraPermission(this))
			{
				ShowLoadingMessage();
				// Note that order matters - see the note in onPause(), the reverse applies here.
				mSession.Resume(mDefaultConfig);
				mSurfaceView.OnResume();
			}
			else
			{
				CameraPermissionHelper.RequestCameraPermission(this);
			}
        }

        protected override void OnPause()
        {
            base.OnPause();
			// Note that the order matters - GLSurfaceView is paused first so that it does not try
			// to query the session. If Session is paused before GLSurfaceView, GLSurfaceView may
			// still call mSession.update() and get a SessionPausedException.
			mSurfaceView.OnPause();
			mSession.Pause();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, global::Android.Content.PM.Permission[] grantResults)
        {
			if (!CameraPermissionHelper.HasCameraPermission(this))
			{
				Toast.MakeText(this,
                               "Camera permission is needed to run this application", ToastLength.Long).Show();
				Finish();
			}
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
			if (hasFocus)
			{
                // Standard Android full-screen functionality.
                Window.DecorView.SystemUiVisibility = 
                    (StatusBarVisibility)(View.SystemUiFlagLayoutStable
                                          | View.SystemUiFlagLayoutHideNavigation
                                          | View.SystemUiFlagLayoutFullscreen
                                          | View.SystemUiFlagHideNavigation
                                          | View.SystemUiFlagFullscreen
                                          | View.SystemUiFlagImmersiveSticky);
                Window.AddFlags(WindowManagerFlags.KeepScreenOn);
			}
        }

		private void onSingleTap(MotionEvent e)
		{
            // Queue tap if there is space. Tap is lost if queue is full.
            if (mQueuedSingleTaps.Count < 16)
            {
                mQueuedSingleTaps.Enqueue(e);
            }
		}

        public void OnSurfaceCreated(IGL10 gl, Javax.Microedition.Khronos.Egl.EGLConfig config)
        {
			GLES20.GlClearColor(0.1f, 0.1f, 0.1f, 1.0f);

			// Create the texture and pass it to ARCore session to be filled during update().
			mBackgroundRenderer.CreateOnGlThread(/*context=*/this);
			mSession.SetCameraTextureName(mBackgroundRenderer.GetTextureId());

			// Prepare the other rendering objects.
			try
			{
				mVirtualObject.CreateOnGlThread(/*context=*/this, "andy.obj", "andy.png");
				mVirtualObject.SetMaterialProperties(0.0f, 3.5f, 1.0f, 6.0f);

				mVirtualObjectShadow.CreateOnGlThread(/*context=*/this,
					"andy_shadow.obj", "andy_shadow.png");
				mVirtualObjectShadow.SetBlendMode(ObjectRenderer.BlendMode.Shadow);
				mVirtualObjectShadow.SetMaterialProperties(1.0f, 0.0f, 0.0f, 1.0f);
			}
			catch (IOException)
			{
                Log.Error(TAG, "Failed to read obj file");
			}
			try
			{
				mPlaneRenderer.CreateOnGlThread(/*context=*/this, "trigrid.png");
			}
			catch (IOException)
			{
                Log.Error(TAG, "Failed to read plane texture");
			}
			mPointCloud.CreateOnGlThread(/*context=*/this);
        }

        public void OnSurfaceChanged(IGL10 gl, int width, int height)
        {
            GLES20.GlViewport(0, 0, width, height);
            // Notify ARCore session that the view size changed so that the perspective matrix and
            // the video background can be properly adjusted.
            mSession.SetDisplayGeometry(width, height);
        }

        public void OnDrawFrame(IGL10 gl)
        {
            // Clear screen to notify driver it should not load any pixels from previous frame.
            GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);

            try
            {
                // Obtain the current frame from ARSession. When the configuration is set to
                // UpdateMode.BLOCKING (it is by default), this will throttle the rendering to the
                // camera framerate.
                Frame frame = mSession.Update();

				// Handle taps. Handling only one tap per frame, as taps are usually low frequency
				// compared to frame rate.
				MotionEvent tap = null;
				mQueuedSingleTaps.TryDequeue(out tap);

                if (tap != null && frame.GetTrackingState() == Frame.TrackingState.Tracking)
                {
                    foreach (HitResult hit in frame.HitTest(tap))
                    {
                        // Check if any plane was hit, and if it was hit inside the plane polygon.
                        if (hit is PlaneHitResult && ((PlaneHitResult)hit).IsHitInPolygon)
                        {
                            // Cap the number of objects created. This avoids overloading both the
                            // rendering system and ARCore.
                            if (mTouches.Count >= 16)
                            {
                                mSession.RemoveAnchors(new List<Anchor> { mTouches[0].GetAnchor() });
                                mTouches.RemoveAt(0);
                            }
                            // Adding an Anchor tells ARCore that it should track this position in
                            // space. This anchor will be used in PlaneAttachment to place the 3d model
                            // in the correct position relative both to the world and to the plane.
                            mTouches.Add(new PlaneAttachment(
                                ((PlaneHitResult)hit).Plane,
                                mSession.AddAnchor(hit.HitPose)));

                            // Hits are sorted by depth. Consider only closest hit on a plane.
                            break;
                        }
                    }
                }

                // Draw background.
                mBackgroundRenderer.Draw(frame);

                // If not tracking, don't draw 3d objects.
                if (frame.GetTrackingState() == Frame.TrackingState.NotTracking)
                {
                    return;
                }

                // Get projection matrix.
                float[] projmtx = new float[16];
                mSession.GetProjectionMatrix(projmtx, 0, 0.1f, 100.0f);

                // Get camera matrix and draw.
                float[] viewmtx = new float[16];
                frame.GetViewMatrix(viewmtx, 0);

                // Compute lighting from average intensity of the image.
                float lightIntensity = frame.LightEstimate.PixelIntensity;

                // Visualize tracked points.
                mPointCloud.Update(frame.PointCloud);
                mPointCloud.Draw(frame.PointCloudPose, viewmtx, projmtx);

                // Check if we detected at least one plane. If so, hide the loading message.
                if (mLoadingMessageSnackbar != null)
                {
                    foreach (Plane plane in mSession.AllPlanes)
                    {
                        if (plane.GetType() == Plane.Type.HorizontalUpwardFacing &&
                            plane.GetTrackingState() == Plane.TrackingState.Tracking)
                        {
                            HideLoadingMessage();
                            break;
                        }
                    }
                }

                // Visualize planes.
                mPlaneRenderer.DrawPlanes(mSession.AllPlanes, frame.Pose, projmtx);

                // Visualize anchors created by touch.
                float scaleFactor = 1.0f;
                foreach (PlaneAttachment planeAttachment in mTouches)
                {
                    if (!planeAttachment.IsTracking())
                    {
                        continue;
                    }
                    // Get the current combined pose of an Anchor and Plane in world space. The Anchor
                    // and Plane poses are updated during calls to session.update() as ARCore refines
                    // its estimate of the world.
                    planeAttachment.GetPose().ToMatrix(mAnchorMatrix, 0);

                    // Update and draw the model and its shadow.
                    mVirtualObject.UpdateModelMatrix(mAnchorMatrix, scaleFactor);
                    mVirtualObjectShadow.UpdateModelMatrix(mAnchorMatrix, scaleFactor);
                    mVirtualObject.Draw(viewmtx, projmtx, lightIntensity);
                    mVirtualObjectShadow.Draw(viewmtx, projmtx, lightIntensity);
                }

            }
            catch (Throwable t)
            {
                // Avoid crashing the application due to unhandled exceptions.
                Log.Error(TAG, "Exception on the OpenGL thread", t);
            }
        }

        private void ShowLoadingMessage()
        {
            RunOnUiThread(() =>
            {
                mLoadingMessageSnackbar = Snackbar.Make(FindViewById(global::Android.Resource.Id.Content), "Searching for surfaces...", Snackbar.LengthIndefinite);
			    unchecked
                {
                    mLoadingMessageSnackbar.View.SetBackgroundColor(new global::Android.Graphics.Color((int)0xbf323232));
                }
                mLoadingMessageSnackbar.Show();
            });
        }

        private void HideLoadingMessage()
        {
            RunOnUiThread(() =>
            {
                mLoadingMessageSnackbar.Dismiss();
                mLoadingMessageSnackbar = null;
            });
        }
    }
}

