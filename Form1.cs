﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using System.IO;
using System.Drawing.Drawing2D;
using Emgu.CV;
using Emgu.Util;
using Emgu.CV.Structure;

namespace Pointboard
{
    public partial class frm_Main : Form
    {
        //Constants
        const int N_CHESSFIELDS_X = 8;
        const int N_CHESSFIELDS_Y = 6;
        const int OFFSET_CHESSBOARD = 7;
        const string FILE_TEST = @"..\..\files\Screenshot.png";

        //Variables
        Image<Gray, Byte> Image_chessboard;
        Image<Bgr, Byte> Image_webcam;
        Image<Bgr, Byte> Image_transformed;
        Image<Gray, Byte> Image_filtered;
        Graphics Drawings;
        Capture Webcam;
        bool Calibrated_perspective = false;
        bool Calibrated_laser = false;
        bool Marking_spot = false;
        bool Mouse_down = false;
        Rectangle Spot;
        Hsv Color_spot;

        HomographyMatrix Transformation_matrix;

        public frm_Main()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            lbl_info.Text = "";

            //Create graphics to draw on box_final
            Drawings = box_final.CreateGraphics();
            Drawings.SmoothingMode = SmoothingMode.AntiAlias;
            
            try
            {
                //Capture Webcam
                Webcam = new Capture();
                Application.Idle += new EventHandler(Show_cam);
            }
            catch
            {
                lbl_info.Text = "Webcam not found. Using testmode";
                Calibrated_perspective = true;
                Application.Idle += new EventHandler(Testmode);
            }
        }

        private void btn_calibrate_perspective_Click(object sender, EventArgs e)
        {
            Calibrated_perspective = Calibrate_perspective();
        }

        private bool Calibrate_perspective()
        {
            if (Image_chessboard == null)
            {//Chessboard-image not loaded yet
                //Load (with same size as original)
                Image_chessboard = new Image<Gray, Byte>(Laserboard.Properties.Resources.Chessboard).Resize(Image_webcam.Width, Image_webcam.Height, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC);
            }

            //Display
            box_final.BackColor = Color.Black;
            box_final.SizeMode = PictureBoxSizeMode.CenterImage;
            box_final.Image = Image_chessboard.Resize(box_final.Width - 2 * OFFSET_CHESSBOARD, box_final.Height - 2 * OFFSET_CHESSBOARD, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC).ToBitmap();
            
            //Get corner-points of original and captured chessboard
            Size size_p = new Size(N_CHESSFIELDS_X - 1, N_CHESSFIELDS_Y - 1);
            Emgu.CV.CvEnum.CALIB_CB_TYPE calibrations = Emgu.CV.CvEnum.CALIB_CB_TYPE.ADAPTIVE_THRESH | Emgu.CV.CvEnum.CALIB_CB_TYPE.NORMALIZE_IMAGE | Emgu.CV.CvEnum.CALIB_CB_TYPE.FILTER_QUADS;
            PointF[] corners_dst = CameraCalibration.FindChessboardCorners(Image_chessboard, size_p, calibrations);
            PointF[] corners_src = CameraCalibration.FindChessboardCorners(Image_webcam.Convert<Gray, Byte>(), size_p, calibrations);
            if (corners_src == null || corners_dst == null) return false; //Chessboard not found

            //Get matrix for transformation
            Transformation_matrix = CameraCalibration.FindHomography(corners_src, corners_dst, Emgu.CV.CvEnum.HOMOGRAPHY_METHOD.DEFAULT, 1);

            //Clear box_final
            box_final.BackColor = Color.Black;
            box_final.SizeMode = PictureBoxSizeMode.StretchImage;
            box_final.Image = null;

            return true; //Successful
        }

        private void btn_calibrate_laser_Click(object sender, EventArgs e)
        {
            btn_calibrate_laser.Enabled = false;
            btn_recalibrate_perspective.Enabled = false;

            //Start marking mode
            box_final.Image = Image_transformed.ToBitmap();
            box_final.Cursor = Cursors.Cross;
            Marking_spot = true;

            //-> Rest is done in box_final_MouseDown(), box_final_MouseMove() and box_final_MouseUp()
        }

        private void box_final_MouseDown(object sender, MouseEventArgs e)
        {
            if (!Marking_spot) return; //Not in marking mode

            Mouse_down = true;

            //Set start position
            Spot.Location = e.Location;
        }

        private Rectangle Norm_rectangle(Rectangle rect)
        {
            //  p1 ___
            //    |   |
            //    '---'p2
            Point p1, p2;
            Rectangle output = new Rectangle();

            //Get origin points
            p1 = rect.Location;
            p2 = new Point(rect.X + rect.Width, rect.Y + rect.Height);

            //Recalculate points
            if (p1.X > p2.X)
            {
                output.X = p2.X;
                output.Width = p1.X - p2.X;
            }
            else
            {
                output.X = p1.X;
                output.Width = p2.X - p1.X;
            }
            if (p1.Y > p2.Y)
            {
                output.Y = p2.Y;
                output.Height = p1.Y - p2.Y;
            }
            else
            {
                output.Y = p1.Y;
                output.Height = p2.Y - p1.Y;
            }

            return output;
        }

        private void box_final_MouseMove(object sender, MouseEventArgs e)
        {
            if (!Marking_spot) return; //Not in marking mode
            if (!Mouse_down) return;

            //Clear
            //Drawings.Clear(box_final.BackColor);
            //box_final.Image = Image_transformed.ToBitmap();
            Drawings.DrawImage(Image_transformed.Resize(box_final.Width, box_final.Height, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC).ToBitmap(), 0, 0);

            //Set size with current position
            Spot.Width = e.X - Spot.X;
            Spot.Height = e.Y - Spot.Y;

            Drawings.DrawRectangle(Pens.White, Norm_rectangle(Spot));
        }

        private void box_final_MouseUp(object sender, MouseEventArgs e)
        {
            if (!Marking_spot) return; //Not in marking mode
            
            Mouse_down = false;

            //Get scale factors
            float factor_x = (float)Image_transformed.Width / box_final.Width;
            float factor_y = (float)Image_transformed.Height / box_final.Height;
            Spot.X = (int)(factor_x * Spot.X);
            Spot.Y = (int)(factor_y * Spot.Y);
            Spot.Width = (int)(factor_x * Spot.Width);
            Spot.Height = (int)(factor_y * Spot.Height);

            //Get average color (HSV) of the spot
            Color_spot = Image_transformed.GetSubRect(Norm_rectangle(Spot)).Convert<Hsv, Byte>().GetAverage();
            //Reset spot position and size
            Spot = new Rectangle();

            //Stop marking mode
            box_final.Image = null;
            Drawings.Clear(box_final.BackColor);
            box_final.Cursor = Cursors.Default;
            Marking_spot = false;
            
            btn_calibrate_laser.Enabled = true;
            btn_recalibrate_perspective.Enabled = true;
            Calibrated_laser = true;
        }

        private void Testmode(object sender, EventArgs e)
        {
            if (Marking_spot) return; //In marking mode

            if (!File.Exists(FILE_TEST))
            {
                lbl_info.Text = "Webcam and test file not found.";
                return;
            }

            box_webcam.BackColor = Color.Gray;

            //Load and display test image
            Image_transformed = new Image<Bgr, Byte>(FILE_TEST);
            box_transformed.Image = Image_transformed.ToBitmap();

            //Clear box_final
            box_final.Image = null;
            box_final.BackColor = Color.Black;

            btn_calibrate_laser.Enabled = true;

            if (Calibrated_laser)
            {
                Filter();
                Find_point();
            }

            //Simulate 30Fps
            System.Threading.Thread.Sleep(33);
        }

        private void Show_cam(object sender, EventArgs e)
        {
            if (Marking_spot) return; //In marking mode

            //Load and display Webcam-image in box_original
            Image_webcam = Webcam.QueryFrame();
            box_webcam.Image = Image_webcam.ToBitmap();

            if (Calibrated_perspective)
            {
                //Transform and display image
                Bgr color_outside = new Bgr(Color.Red); //Detect/change later
                Image_transformed = Image_webcam.WarpPerspective(Transformation_matrix, Emgu.CV.CvEnum.INTER.CV_INTER_CUBIC, Emgu.CV.CvEnum.WARP.CV_WARP_FILL_OUTLIERS, color_outside);
                box_transformed.Image = Image_transformed.ToBitmap();

                btn_calibrate_laser.Enabled = true;

                if (Calibrated_laser)
                {
                    Filter();
                    Find_point();
                }
            }
            else
            {
                Calibrated_perspective = Calibrate_perspective();
                if(Calibrated_perspective) btn_recalibrate_perspective.Enabled = true;
            }
        }

        private void Filter()
        {
            if (Calibrated_laser)
            {
                //Create thresholds
                Hsv threshold_lower = new Hsv(Color_spot.Hue - 25, 100, 100);
                Hsv threshold_higher = new Hsv(Color_spot.Hue + 25, 240, 240);

                //Blur image and find colors between thresholds
                Image_filtered = Image_transformed.Convert<Hsv, Byte>().SmoothBlur(20, 20).InRange(threshold_lower, threshold_higher);

                //Reduce size of the spot and display image
                Image_filtered = Image_filtered.Erode(4);
                box_filtered.Image = Image_filtered.ToBitmap();
            }
        }

        private void Find_point()
        {
            float factor_x;
            float factor_y;
            float circle_x;
            float circle_y;
            float diameter;

            //Clear image
            Drawings.Clear(box_final.BackColor);

            //Find Circles
            CircleF[] circles = Image_filtered.HoughCircles(
            new Gray(180), //The higher threshold of the two passed to Canny edge detector (the lower one will be twice smaller)
            new Gray(6), //Accumulator threshold at the center detection stage
            1.0, //Resolution of the accumulator used to detect centers of the circles
            10.0, //Min distance
            2, //Min radius
            20 //Max radius
            )[0]; //Get the circles from the first channel

            //Mark first circle
            if (circles.Length > 0)
            {
                Pen pen_circle = new Pen(Color.Blue, 3);

                //Get scale factors
                factor_x = (float)box_final.Width / Image_filtered.Width;
                factor_y = (float)box_final.Height / Image_filtered.Height;

                //Calculate coordinates and diameter
                circle_x = circles[0].Center.X - circles[0].Radius;
                circle_y = circles[0].Center.Y - circles[0].Radius;
                diameter = 2 * (circles[0].Radius + pen_circle.Width);

                //Convert coordinates for picturebox box_final
                circle_x *= factor_x;
                circle_y *= factor_y;

                lbl_info.Text = circle_x.ToString() + " " + circle_y.ToString();

                Drawings.DrawEllipse(pen_circle, circle_x, circle_y, diameter, diameter);
            }

            /*Mark multiple circles
            //int circle_number = 0;
            //lbl_info.Text = "";
            //foreach (CircleF circle in circles)
            {
                if (circle_number >= 3)
                {
                    lbl_info.Text += " +";
                    return;
                }
                circle_number++;
                lbl_info.Text += "(" + circle.Center.X + "; " + circle.Center.Y + ")  ";
                Grafik.DrawEllipse(circlepen, circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius, circle.Radius * 2, circle.Radius * 2);
            }*/
        }

        private void box_images_MouseEnter(object sender, EventArgs e)
        {
            //Handle cursors
            if (box_webcam.Image != null)
            {
                box_webcam.Cursor = Cursors.Hand;
            }
            else
            {
                box_webcam.Cursor = Cursors.Default;
            }

            if (box_transformed.Image != null)
            {
                box_transformed.Cursor = Cursors.Hand;
            }
            else
            {
                box_transformed.Cursor = Cursors.Default;
            }

            if (box_filtered.Image != null)
            {
                box_filtered.Cursor = Cursors.Hand;
            }
            else
            {
                box_filtered.Cursor = Cursors.Default;
            }
        }

        private void box_original_Click(object sender, EventArgs e)
        {
            if (box_webcam.Image != null)
            {
                sfd_screenshot.Tag = box_webcam;
                sfd_screenshot.FileName = "Screenshot_webcam";
                sfd_screenshot.ShowDialog();
            }
        }

        private void box_transformed_Click(object sender, EventArgs e)
        {
            if (box_transformed.Image != null)
            {
                sfd_screenshot.Tag = box_transformed;
                sfd_screenshot.FileName = "Screenshot_transformed";
                sfd_screenshot.ShowDialog();
            }
        }

        private void box_filtered_Click(object sender, EventArgs e)
        {
            if (box_filtered.Image != null)
            {
                sfd_screenshot.Tag = box_filtered;
                sfd_screenshot.FileName = "Screenshot_filtered";
                sfd_screenshot.ShowDialog();
            }
        }

        private void sfd_Screenshot_FileOk(object sender, CancelEventArgs e)
        {
            if (sfd_screenshot.Tag == box_webcam)
            {
                box_webcam.Image.Save(sfd_screenshot.FileName);
            }
            else if (sfd_screenshot.Tag == box_transformed)
            {
                box_transformed.Image.Save(sfd_screenshot.FileName);
            }
            else if (sfd_screenshot.Tag == box_filtered)
            {
                box_filtered.Image.Save(sfd_screenshot.FileName);
            }
        }

        private void frm_Pointboard_FormClosing(object sender, FormClosingEventArgs e)
        {
            Dispose();
        }
    }
}
