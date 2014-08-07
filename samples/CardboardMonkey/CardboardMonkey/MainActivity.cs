using System;
using System.IO;
using System.Linq;

using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Opengl;
using Android.Util;

using Google.VRToolkit.Carboard;
using Google.VRToolkit.Carboard.Sensors;

using Java.Nio;

namespace CardboardMonkey
{
	[Activity (Label = "CardboardMonkey", MainLauncher = true, Icon = "@drawable/g_cardboard_icon",
	           ScreenOrientation = Android.Content.PM.ScreenOrientation.Landscape)]
	public class MainActivity : CardboardActivity, CardboardView.IStereoRenderer
	{
		const string Tag = "MainActivity";

		const float CameraZ = 0.01f;
		const float TimeDelta = 0.3f;

		const float YawLimit = 0.12f;
		const float PitchLimit = 0.12f;

		// We keep the light always position just above the user.
		readonly float[] lightPosInWorldSpace = new float[] {0.0f, 2.0f, 0.0f, 1.0f};
		readonly float[] lightPosInEyeSpace = new float[4];

		const int CoordsPerVertex = 3;

		FloatBuffer floorVertices;
		FloatBuffer floorColors;
		FloatBuffer floorNormals;

		FloatBuffer cubeVertices;
		FloatBuffer cubeColors;
		FloatBuffer cubeFoundColors;
		FloatBuffer cubeTextureCoords;
		FloatBuffer cubeNormals;

		int glProgram;
		int positionParam;
		int normalParam;
		int colorParam;
		int modelViewProjectionParam;
		int lightPosParam;
		int modelViewParam;
		int modelParam;
		int isFloorParam;
		int texture;
		int texCoordParam;

		int monkeyNotFound;
		int monkeyFound;

		float[] modelCube;
		float[] camera;
		float[] view;
		float[] headView;
		float[] modelViewProjection;
		float[] modelView;

		float[] modelFloor;

		int mScore = 0;
		float mObjectDistance = 12f;
		float mFloorDepth = 20f;

		Vibrator mVibrator;

		CardboardOverlayView mOverlayView;
		Random rnd = new Random ();

		int LoadGlShader (int type, int resId)
		{
			string code = ReadRawTextFile (resId);
			int shader = GLES20.GlCreateShader(type);
			GLES20.GlShaderSource(shader, code);
			GLES20.GlCompileShader(shader);

			// Get the compilation status.
			int[] compileStatus = new int[1];
			GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, compileStatus, 0);

			// If the compilation failed, delete the shader.
			if (compileStatus[0] == 0) {
				Log.Error(Tag, "Error compiling shader: " + GLES20.GlGetShaderInfoLog(shader));
				GLES20.GlDeleteShader(shader);
				shader = 0;
			}

			if (shader == 0)
				throw new InvalidOperationException("Error creating shader.");

			return shader;
		}

		int LoadGlTexture (int resId)
		{
			var texture = new int[1];
			GLES20.GlGenTextures (1, texture, 0);
			if (texture [0] == 0)
				throw new InvalidOperationException ("Can't create texture");
			var options = new Android.Graphics.BitmapFactory.Options {
				InScaled = false
			};
			var bmp = Android.Graphics.BitmapFactory.DecodeResource (Resources, resId, options);
			GLES20.GlBindTexture (GLES20.GlTexture2d, texture [0]);
			GLES20.GlTexParameteri (GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlNearest);
			GLES20.GlTexParameteri (GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlNearest);

			GLUtils.TexImage2D (GLES20.GlTexture2d, 0, bmp, 0);
			bmp.Recycle ();

			return texture [0];
		}

		static void CheckGlError (string func)
		{
			int error;
			while ((error = GLES20.GlGetError()) != GLES20.GlNoError) {
				Log.Error(Tag, func + ": GlError " + error);
				throw new InvalidOperationException(func + ": GlError " + error);
			}
		}

		protected override void OnCreate (Bundle savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			SetContentView(Resource.Layout.common_ui);
			CardboardView cardboardView = FindViewById<CardboardView> (Resource.Id.cardboard_view);
			cardboardView.SetRenderer (this);
			CardboardView = cardboardView;

			modelCube = new float[16];
			camera = new float[16];
			view = new float[16];
			modelViewProjection = new float[16];
			modelView = new float[16];
			modelFloor = new float[16];
			headView = new float[16];
			mVibrator = (Vibrator)GetSystemService(Context.VibratorService);

			mOverlayView = FindViewById<CardboardOverlayView> (Resource.Id.overlay);
			mOverlayView.show3DToast("Pull the magnet when you find an object.");
		}

		public void OnRendererShutdown ()
		{
			Log.Info (Tag, "onRendererShutdown");
		}

		public void OnSurfaceChanged (int width, int height)
		{
			Log.Info (Tag, "onSurfaceChanged");
		}

		public void OnSurfaceCreated (Javax.Microedition.Khronos.Egl.EGLConfig config)
		{
			Log.Info (Tag, "onSurfaceCreated");
			GLES20.GlClearColor(0.1f, 0.1f, 0.1f, 0.5f); // Dark background so text shows up well

			cubeVertices = PrepareBuffer (WorldLayoutData.CubeCoords);
			cubeColors = PrepareBuffer (WorldLayoutData.CubeColors);
			cubeFoundColors = PrepareBuffer (WorldLayoutData.CubeFoundColors);
			cubeNormals = PrepareBuffer (WorldLayoutData.CubeNormals);
			cubeTextureCoords = PrepareBuffer (WorldLayoutData.CubeTexCoords);

			floorVertices = PrepareBuffer (WorldLayoutData.FloorCoords);
			floorNormals = PrepareBuffer (WorldLayoutData.FloorNormals);
			floorColors = PrepareBuffer (WorldLayoutData.FloorColors);

			monkeyFound = LoadGlTexture (Resource.Drawable.texture2);
			monkeyNotFound = LoadGlTexture (Resource.Drawable.texture1);

			int vertexShader = LoadGlShader(GLES20.GlVertexShader, Resource.Raw.vertex);
			int gridShader = LoadGlShader(GLES20.GlFragmentShader, Resource.Raw.fragment);

			glProgram = GLES20.GlCreateProgram();
			GLES20.GlAttachShader(glProgram, vertexShader);
			GLES20.GlAttachShader(glProgram, gridShader);
			GLES20.GlLinkProgram(glProgram);

			GLES20.GlEnable(GLES20.GlDepthTest);

			// Object first appears directly in front of user
			Matrix.SetIdentityM(modelCube, 0);
			Matrix.TranslateM(modelCube, 0, 0, 0, -mObjectDistance);

			Matrix.SetIdentityM(modelFloor, 0);
			Matrix.TranslateM(modelFloor, 0, 0, -mFloorDepth, 0); // Floor appears below user

			CheckGlError("onSurfaceCreated");
		}

		FloatBuffer PrepareBuffer (float[] data)
		{
			ByteBuffer buffer = ByteBuffer.AllocateDirect (data.Length * 4);
			buffer.Order(ByteOrder.NativeOrder());
			var result = buffer.AsFloatBuffer();
			result.Put (data);
			result.Position(0);

			return result;
		}

		string ReadRawTextFile (int resId)
		{
			return new StreamReader (Resources.OpenRawResource (resId)).ReadToEnd ();
		}

		public void OnNewFrame (HeadTransform headTransform)
		{
			GLES20.GlUseProgram(glProgram);

			modelViewProjectionParam = GLES20.GlGetUniformLocation(glProgram, "u_MVP");
			lightPosParam = GLES20.GlGetUniformLocation(glProgram, "u_LightPos");
			modelViewParam = GLES20.GlGetUniformLocation(glProgram, "u_MVMatrix");
			modelParam = GLES20.GlGetUniformLocation(glProgram, "u_Model");
			isFloorParam = GLES20.GlGetUniformLocation(glProgram, "u_IsFloor");
			texture = GLES20.GlGetUniformLocation (glProgram, "u_texture");

			// Build the Model part of the ModelView matrix.
			Matrix.RotateM(modelCube, 0, TimeDelta, 0.5f, 0.5f, 1.0f);

			// Build the camera matrix and apply it to the ModelView.
			Matrix.SetLookAtM(camera, 0, 0.0f, 0.0f, CameraZ, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f);

			headTransform.GetHeadView(headView, 0);

			CheckGlError("onReadyToDraw");
		}

		public void OnDrawEye (EyeTransform transform)
		{
			GLES20.GlClear(GLES20.GlColorBufferBit | GLES20.GlDepthBufferBit);

			positionParam = GLES20.GlGetAttribLocation(glProgram, "a_Position");
			normalParam = GLES20.GlGetAttribLocation(glProgram, "a_Normal");
			colorParam = GLES20.GlGetAttribLocation(glProgram, "a_Color");
			texCoordParam = GLES20.GlGetAttribLocation (glProgram, "a_texcoord");

			GLES20.GlEnableVertexAttribArray(positionParam);
			GLES20.GlEnableVertexAttribArray(normalParam);
			GLES20.GlEnableVertexAttribArray(colorParam);
			GLES20.GlEnableVertexAttribArray(texCoordParam);
			CheckGlError("mColorParam");

			// Apply the eye transformation to the camera.
			Matrix.MultiplyMM(view, 0, transform.GetEyeView(), 0, camera, 0);

			// Set the position of the light
			Matrix.MultiplyMV(lightPosInEyeSpace, 0, view, 0, lightPosInWorldSpace, 0);
			GLES20.GlUniform3f(lightPosParam, lightPosInEyeSpace[0], lightPosInEyeSpace[1],
			               lightPosInEyeSpace[2]);

			// Build the ModelView and ModelViewProjection matrices
			// for calculating cube position and light.
			Matrix.MultiplyMM(modelView, 0, view, 0, modelCube, 0);
			Matrix.MultiplyMM(modelViewProjection, 0, transform.GetPerspective(), 0, modelView, 0);
			DrawCube();

			// Set mModelView for the floor, so we draw floor in the correct location
			Matrix.MultiplyMM(modelView, 0, view, 0, modelFloor, 0);
			Matrix.MultiplyMM(modelViewProjection, 0, transform.GetPerspective(), 0,
			              modelView, 0);
			DrawFloor(transform.GetPerspective ());
		}

		public void OnFinishFrame (Viewport viewport)
		{
		}

		public void DrawCube ()
		{
			// This is not the floor!
			GLES20.GlUniform1f(isFloorParam, 0f);

			// Set the Model in the shader, used to calculate lighting
			GLES20.GlUniformMatrix4fv(modelParam, 1, false, modelCube, 0);

			// Set the ModelView in the shader, used to calculate lighting
			GLES20.GlUniformMatrix4fv(modelViewParam, 1, false, modelView, 0);

			// Set the position of the cube
			GLES20.GlVertexAttribPointer(positionParam, CoordsPerVertex, GLES20.GlFloat,
			                         false, 0, cubeVertices);

			// Set the ModelViewProjection matrix in the shader.
			GLES20.GlUniformMatrix4fv(modelViewProjectionParam, 1, false, modelViewProjection, 0);

			// Set the normal positions of the cube, again for shading
			GLES20.GlVertexAttribPointer(normalParam, 3, GLES20.GlFloat,
			                         false, 0, cubeNormals);

			// Set the texture coordinates
			GLES20.GlVertexAttribPointer (texCoordParam, 2, GLES20.GlFloat, false, 0, cubeTextureCoords);

			GLES20.GlActiveTexture (GLES20.GlTexture0);

			if (IsLookingAtObject) {
				GLES20.GlVertexAttribPointer (colorParam, 4, GLES20.GlFloat, false,
				                              0, cubeFoundColors);
				GLES20.GlBindTexture (GLES20.GlTexture2d, monkeyFound);
			} else {
				GLES20.GlVertexAttribPointer (colorParam, 4, GLES20.GlFloat, false,
				                              0, cubeColors);
				GLES20.GlBindTexture (GLES20.GlTexture2d, monkeyNotFound);
			}

			GLES20.GlUniform1i (texture, 0);

			GLES20.GlDrawArrays(GLES20.GlTriangles, 0, 36);
			CheckGlError("Drawing cube");
		}

		public void DrawFloor(float[] perspective)
		{
			// This is the floor!
			GLES20.GlUniform1f(isFloorParam, 1f);

			// Set ModelView, MVP, position, normals, and color
			GLES20.GlUniformMatrix4fv(modelParam, 1, false, modelFloor, 0);
			GLES20.GlUniformMatrix4fv(modelViewParam, 1, false, modelView, 0);
			GLES20.GlUniformMatrix4fv(modelViewProjectionParam, 1, false, modelViewProjection, 0);
			GLES20.GlVertexAttribPointer(positionParam, CoordsPerVertex, GLES20.GlFloat,
			                         false, 0, floorVertices);
			GLES20.GlVertexAttribPointer(normalParam, 3, GLES20.GlFloat, false, 0, floorNormals);
			GLES20.GlVertexAttribPointer(colorParam, 4, GLES20.GlFloat, false, 0, floorColors);
			GLES20.GlDrawArrays(GLES20.GlTriangles, 0, 6);

			CheckGlError("drawing floor");
		}

		public override void OnCardboardTrigger()
		{
			Log.Info (Tag, "onCardboardTrigger");

			if (IsLookingAtObject) {
				mScore++;
				mOverlayView.show3DToast("Found it! Look around for another one.\nScore = " + mScore);
				HideObject();
			} else {
				mOverlayView.show3DToast("Look around to find the object!");
			}
			// Always give user feedback
			mVibrator.Vibrate(50);
		}

		void HideObject()
		{
			float[] rotationMatrix = new float[16];
			float[] posVec = new float[4];

			// First rotate in XZ plane, between 90 and 270 deg away, and scale so that we vary
			// the object's distance from the user.
			float anGleXZ = (float)rnd.NextDouble () * 180 + 90;
			Matrix.SetRotateM(rotationMatrix, 0, anGleXZ, 0f, 1f, 0f);
			float oldObjectDistance = mObjectDistance;
			mObjectDistance = (float)rnd.NextDouble () * 15 + 5;
			float objectScalingFactor = mObjectDistance / oldObjectDistance;
			Matrix.ScaleM(rotationMatrix, 0, objectScalingFactor, objectScalingFactor, objectScalingFactor);
			Matrix.MultiplyMV(posVec, 0, rotationMatrix, 0, modelCube, 12);

			// Now get the up or down anGle, between -20 and 20 degrees
			float anGleY = (float)rnd.NextDouble () * 80 - 40; // anGle in Y plane, between -40 and 40
			anGleY = (float)(anGleY * Math.PI) / 180;
			float newY = (float)Math.Tan(anGleY) * mObjectDistance;

			Matrix.SetIdentityM(modelCube, 0);
			Matrix.TranslateM(modelCube, 0, posVec[0], newY, posVec[2]);
		}

		bool IsLookingAtObject {
			get {
				float[] initVec = { 0, 0, 0, 1.0f };
				float[] objPositionVec = new float[4];
			
				// Convert object space to camera space. Use the headView from onNewFrame.
				Matrix.MultiplyMM (modelView, 0, headView, 0, modelCube, 0);
				Matrix.MultiplyMV (objPositionVec, 0, modelView, 0, initVec, 0);

				float pitch = (float)Math.Atan2 (objPositionVec [1], -objPositionVec [2]);
				float yaw = (float)Math.Atan2 (objPositionVec [0], -objPositionVec [2]);

				Log.Info (Tag, "Object position: X: " + objPositionVec [0] + "  Y: " + objPositionVec [1] + " Z: " + objPositionVec [2]);
				Log.Info (Tag, "Object Pitch: " + pitch + "  Yaw: " + yaw);

				return (Math.Abs (pitch) < PitchLimit) && (Math.Abs (yaw) < YawLimit);
			}
		}
	}
}


