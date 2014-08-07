using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.Views.Animations;
using Android.Graphics;

namespace CardboardMonkey
{
	public class CardboardOverlayView : LinearLayout
	{
		CardboardOverlayEyeView mLeftView;
		CardboardOverlayEyeView mRightView;
		AlphaAnimation mTextFadeAnimation;

		public CardboardOverlayView (Context context) :
			base (context)
		{
			Initialize ();
		}

		public CardboardOverlayView (Context context, IAttributeSet attrs) :
			base (context, attrs)
		{
			Initialize ();
		}

		public CardboardOverlayView (Context context, IAttributeSet attrs, int defStyle) :
			base (context, attrs, defStyle)
		{
			Initialize ();
		}

		void Initialize ()
		{
			Orientation = Orientation.Horizontal;

			var ps = new LinearLayout.LayoutParams (LinearLayout.LayoutParams.MatchParent,
			                                        LinearLayout.LayoutParams.MatchParent,
			                                        1.0f);
			ps.SetMargins (0, 0, 0, 0);

			mLeftView = new CardboardOverlayEyeView (Context);
			mLeftView.LayoutParameters = ps;
			AddView(mLeftView);

			mRightView = new CardboardOverlayEyeView (Context);
			mRightView.LayoutParameters = ps;
			AddView(mRightView);

			// Set some reasonable defaults.
			SetDepthOffset(0.016f);

			SetColor(Color.Rgb (150, 255, 180));
			Visibility = ViewStates.Visible;

			mTextFadeAnimation = new AlphaAnimation(1.0f, 0.0f);
			mTextFadeAnimation.Duration = 5000;
		}

		public void show3DToast(string message)
		{
			SetText (message);
			SetTextAlpha (1f);
			mTextFadeAnimation.AnimationEnd += delegate {
				SetTextAlpha (0f);
			};
			StartAnimation (mTextFadeAnimation);
		}

		void SetDepthOffset(float offset) {
			mLeftView.SetOffset(offset);
			mRightView.SetOffset(-offset);
		}

		void SetText(string text) {
			mLeftView.SetText (text);
			mRightView.SetText (text);
		}

		void SetTextAlpha(float alpha) {
			mLeftView.SetTextViewAlpha(alpha);
			mRightView.SetTextViewAlpha(alpha);
		}

		void SetColor(Color color) {
			mLeftView.SetColor (color);
			mRightView.SetColor (color);
		}

		class CardboardOverlayEyeView: ViewGroup
		{
			readonly ImageView imageView;
			readonly TextView textView;
			float offset;

			public CardboardOverlayEyeView (Context context) : base (context)
			{
				imageView = new ImageView(context);
				imageView.SetScaleType (ImageView.ScaleType.CenterInside);
				imageView.SetAdjustViewBounds (true);  // Preserve aspect ratio.
				AddView(imageView);

				textView = new TextView(context);
				textView.SetTextSize (ComplexUnitType.Dip, 14f);
				textView.SetTypeface (textView.Typeface, TypefaceStyle.Bold);
				textView.Gravity = GravityFlags.Center;
				textView.SetShadowLayer(3.0f, 0.0f, 0.0f, Color.DarkGray);
				AddView(textView);
			}

			public void SetColor(Color color) {
				imageView.SetColorFilter (color);
				textView.SetTextColor (color);
			}

			public void SetText(string text)
			{
				textView.Text = text;
			}

			public void SetTextViewAlpha(float alpha)
			{
				textView.Alpha = alpha;
			}

			public void SetOffset(float offset)
			{
				this.offset = offset;
			}

			protected override void OnLayout (bool changed, int left, int top, int right, int bottom)
			{
				// Width and height of this ViewGroup.
				int width = right - left;
				int height = bottom - top;

				// The size of the image, given as a fraction of the dimension as a ViewGroup. We multiply
				// both width and heading with this number to compute the image's bounding box. Inside the
				// box, the image is the horizontally and vertically centered.
				float imageSize = 0.12f;

				// The fraction of this ViewGroup's height by which we shift the image off the ViewGroup's
				// center. Positive values shift downwards, negative values shift upwards.
				float verticalImageOffset = -0.07f;

				// Vertical position of the text, specified in fractions of this ViewGroup's height.
				float verticalTextPos = 0.52f;

				// Layout ImageView
				float imageMargin = (1.0f - imageSize) / 2.0f;
				float leftMargin = (int) (width * (imageMargin + offset));
				float topMargin = (int) (height * (imageMargin + verticalImageOffset));
				imageView.Layout((int) leftMargin, (int) topMargin,
				                 (int) (leftMargin + width * imageSize), (int) (topMargin + height * imageSize));

				// Layout TextView
				leftMargin = offset * width;
				topMargin = height * verticalTextPos;
				textView.Layout ((int) leftMargin, (int) topMargin,
				                 (int) (leftMargin + width), (int) (topMargin + height * (1.0f - verticalTextPos)));
			}
		}
	}
}

