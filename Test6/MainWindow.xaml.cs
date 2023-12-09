using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DeckLinkAPI;
using OpenCvSharp;

namespace Test6
{

    public partial class MainWindow : System.Windows.Window
    {
        private DeckLinkDeviceDiscovery m_deckLinkDeviceDiscovery;
        private DeckLinkDevice m_inputDevice;
        private DeckLinkOutputDevice m_outputDevice;
        private ProfileCallback m_profileCallback;

        private CaptureCallback m_captureCallback;
        private PlaybackCallback m_playbackCallback;

        public MainWindow()
        {
            InitializeComponent();

            m_profileCallback = new ProfileCallback();
            m_captureCallback = new CaptureCallback();
            m_playbackCallback = new PlaybackCallback();

            m_captureCallback.FrameReceived += OnFrameReceived;

            m_deckLinkDeviceDiscovery = new DeckLinkDeviceDiscovery();
            m_deckLinkDeviceDiscovery.DeviceArrived += AddDevice;
            m_deckLinkDeviceDiscovery.Enable();
        }

        private void AddDevice(object sender, DeckLinkDiscoveryEventArgs e)
        {
            var deviceName = DeckLinkDeviceTools.GetDisplayLabel(e.deckLink);
            if (deviceName.Contains("DeckLink Duo (2)"))
            {
                m_inputDevice = new DeckLinkDevice(e.deckLink, m_profileCallback);
                InitializeInputDevice();
            }
            else if (deviceName.Contains("DeckLink Duo (4)"))
            {
                m_outputDevice = new DeckLinkOutputDevice(e.deckLink, m_profileCallback);
                InitializeOutputDevice();
            }
        }


        private void InitializeInputDevice()
        {
            if (m_inputDevice != null)
            {
                m_inputDevice.StartCapture(_BMDDisplayMode.bmdModeHD1080p5994, m_captureCallback, false);
            }
        }

        private void InitializeOutputDevice()
        {
            if (m_outputDevice != null)
            {
                m_outputDevice.PrepareForPlayback(_BMDDisplayMode.bmdModeHD1080p6000, m_playbackCallback);
            }

        }


        private void OnFrameReceived(IDeckLinkVideoInputFrame videoFrame)
        {
            IntPtr frameBytes;
            videoFrame.GetBytes(out frameBytes);

            int width = videoFrame.GetWidth();
            int height = videoFrame.GetHeight();

            using (Mat capturedFrame = new Mat(height, width, OpenCvSharp.MatType.CV_8UC2, frameBytes))
            {
                Mat processedFrame = ProcessFrameWithOpenCV(capturedFrame);

                if (m_outputDevice != null)
                {
                    m_outputDevice.ScheduleFrame(processedFrame);
                }
            }

        }

        private Mat ProcessFrameWithOpenCV(Mat inputFrame)
        {
            double alpha = 1;
            double beta = 0;
            //Mat contrastAdjustedFrame = AdjustContrastUYVY(inputFrame, alpha, beta);

            //Mat finalFrame = DrawRectangle(contrastAdjustedFrame);
            Mat finalFrame = DrawRectangle(inputFrame);

            return finalFrame;
        }

        private Mat DrawRectangle(Mat inputFrame)
        {
            int rectWidth = 400;
            int rectHeight = 250;
            int centerX = inputFrame.Width / 2;
            int centerY = inputFrame.Height / 2;

            int leftX = centerX - rectWidth / 2;
            int rightX = centerX + rectWidth / 2;
            int bottomY = centerY + rectHeight / 2;

            OpenCvSharp.Point leftTop = new OpenCvSharp.Point(leftX, centerY - rectHeight / 2);
            OpenCvSharp.Point leftBottom = new OpenCvSharp.Point(leftX, bottomY);
            OpenCvSharp.Point rightTop = new OpenCvSharp.Point(rightX, centerY - rectHeight / 2);
            OpenCvSharp.Point rightBottom = new OpenCvSharp.Point(rightX, bottomY);

            Scalar greenColor = new Scalar(0, 255, 0);
            int thickness = 2;

            Cv2.Line(inputFrame, leftTop, leftBottom, greenColor, thickness);
            Cv2.Line(inputFrame, rightTop, rightBottom, greenColor, thickness);

            Cv2.Line(inputFrame, leftBottom, rightBottom, greenColor, thickness);

            return inputFrame;
        }

        //private Mat AdjustContrastUYVY(Mat uyvyFrame, double alpha, double beta)
        //{
        //    // UYVY format: U0 Y0 V0 Y1 (for each two pixels)
        //    // Create a clone to work on
        //    Mat adjustedFrame = uyvyFrame.Clone();

        //    unsafe
        //    {
        //        byte* dataPtr = (byte*)adjustedFrame.DataPointer;
        //        int totalBytes = uyvyFrame.Rows * uyvyFrame.Cols * uyvyFrame.ElemSize();

        //        for (int i = 0; i < totalBytes; i += 4)
        //        {
        //            // Adjust Y values at positions 1 and 3 in the UYVY sequence
        //            for (int j = 1; j <= 3; j += 2)
        //            {
        //                int yValue = dataPtr[i + j];
        //                // Adjust the Y value
        //                yValue = (int)(alpha * yValue + beta);
        //                // Clamp the value to 0-255 range
        //                yValue = Math.Max(0, Math.Min(255, yValue));
        //                dataPtr[i + j] = (byte)yValue;
        //            }
        //        }
        //    }

        //    return adjustedFrame;
        //}




        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (m_inputDevice != null)
            {
                m_inputDevice.StopCapture();
            }

            if (m_outputDevice != null)
            {
                m_outputDevice.StopPlayback();
            }

            m_deckLinkDeviceDiscovery.Disable();
        }
    }
    public class CaptureCallback : IDeckLinkInputCallback
    {
        public event Action<IDeckLinkVideoInputFrame> FrameReceived;

        public void VideoInputFrameArrived(IDeckLinkVideoInputFrame videoFrame, IDeckLinkAudioInputPacket audioPacket)
        {
            FrameReceived?.Invoke(videoFrame);
            // Here you can process the video frame or audio packet
        }

        public void VideoInputFormatChanged(_BMDVideoInputFormatChangedEvents notificationEvents, IDeckLinkDisplayMode newDisplayMode, _BMDDetectedVideoInputFormatFlags detectedSignalFlags)
        {
            // Handle format changes here
        }
    }

    public class PlaybackCallback : IDeckLinkVideoOutputCallback
    {
        public event Action<IDeckLinkVideoFrame, _BMDOutputFrameCompletionResult> FrameCompleted;

        public void ScheduledFrameCompleted(IDeckLinkVideoFrame completedFrame, _BMDOutputFrameCompletionResult result)
        {
            FrameCompleted?.Invoke(completedFrame, result);
            // Here you can handle the completion of the frame playback
        }

        public void ScheduledPlaybackHasStopped()
        {
            // Handle playback stop here
        }
    }
}
