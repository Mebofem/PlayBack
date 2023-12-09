using DeckLinkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test6
{
    class DeckLinkInputInvalidException : Exception { }
    class DeckLinkStartCaptureException : Exception { }

    public class DeckLinkDeviceInputVideoFrameEventArgs : EventArgs
    {
        public readonly IDeckLinkVideoInputFrame videoFrame;

        public DeckLinkDeviceInputVideoFrameEventArgs(IDeckLinkVideoInputFrame videoFrame)
        {
            this.videoFrame = videoFrame;
        }
    }

    public class DeckLinkDeviceInputFormatEventArgs : EventArgs
    {
        public readonly _BMDDisplayMode displayMode;
        public readonly bool dualStream3D;

        public DeckLinkDeviceInputFormatEventArgs(_BMDDisplayMode displayMode, bool dualStream3D)
        {
            this.displayMode = displayMode;
            this.dualStream3D = dualStream3D;
        }
    }

    class DeckLinkDevice : IDeckLinkInputCallback
    {
        private readonly IDeckLink m_deckLink;
        private readonly IDeckLinkInput m_deckLinkInput;
        private readonly IDeckLinkConfiguration m_deckLinkConfiguration;
        private readonly IDeckLinkProfileManager m_deckLinkProfileManager;

        private bool m_applyDetectedInputMode = true;
        private bool m_currentlyCapturing = false;

        private readonly bool m_supportsInputFormatDetection;
        private readonly long m_availableInputConnections;
        private readonly string m_displayName;

        public event EventHandler<DeckLinkDeviceInputVideoFrameEventArgs> VideoFrameArrived;
        public event EventHandler<DeckLinkDeviceInputFormatEventArgs> InputFormatChanged;

        public DeckLinkDevice(IDeckLink deckLink, IDeckLinkProfileCallback profileCallback)
        {
            m_deckLink = deckLink;

            var deckLinkAttributes = m_deckLink as IDeckLinkProfileAttributes;
            deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkVideoIOSupport, out long ioSupportAttribute);
            if (!((_BMDVideoIOSupport)ioSupportAttribute).HasFlag(_BMDVideoIOSupport.bmdDeviceSupportsCapture))
                throw new DeckLinkInputInvalidException();

            deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkVideoInputConnections, out m_availableInputConnections);

            deckLinkAttributes.GetFlag(_BMDDeckLinkAttributeID.BMDDeckLinkSupportsInputFormatDetection, out int inputFormatDetectionAttribute);
            m_supportsInputFormatDetection = Convert.ToBoolean(inputFormatDetectionAttribute);

            m_deckLinkInput = m_deckLink as IDeckLinkInput;
 
            m_deckLinkConfiguration = m_deckLink as IDeckLinkConfiguration;

            m_displayName = DeckLinkDeviceTools.GetDisplayLabel(m_deckLink);

            m_deckLinkProfileManager = m_deckLink as IDeckLinkProfileManager;
            m_deckLinkProfileManager?.SetCallback(profileCallback);
        }

        ~DeckLinkDevice()
        {
            m_deckLinkProfileManager?.SetCallback(null);
        }

        public IDeckLink DeckLink => m_deckLink;
        public IDeckLinkInput DeckLinkInput => m_deckLinkInput;
        public IDeckLinkConfiguration DeckLinkConfiguration => m_deckLinkConfiguration;
        public string DisplayName => m_displayName;
        public _BMDVideoConnection AvailableInputConnections => (_BMDVideoConnection)m_availableInputConnections;
        public bool SupportsFormatDetection => m_supportsInputFormatDetection;
        public bool IsCapturing => m_currentlyCapturing;
        public bool IsActive => DeckLinkDeviceTools.IsDeviceActive(m_deckLink);

        public _BMDVideoConnection CurrentVideoInputConnection
        {
            get
            {
                m_deckLinkConfiguration.GetInt(_BMDDeckLinkConfigurationID.bmdDeckLinkConfigVideoInputConnection, out long currentInputConnection);
                return (_BMDVideoConnection)currentInputConnection;
            }
            set
            {
                m_deckLinkConfiguration.SetInt(_BMDDeckLinkConfigurationID.bmdDeckLinkConfigVideoInputConnection, (long)value);
            }
        }

        public IEnumerable<IDeckLinkDisplayMode> DisplayModes
        {
            get
            {
                m_deckLinkInput.GetDisplayModeIterator(out IDeckLinkDisplayModeIterator displayModeIterator);
                if (displayModeIterator == null)
                    yield break;

                while (true)
                {
                    displayModeIterator.Next(out IDeckLinkDisplayMode displayMode);

                    if (displayMode != null)
                    {
                        m_deckLinkInput.DoesSupportVideoMode(CurrentVideoInputConnection, displayMode.GetDisplayMode(), _BMDPixelFormat.bmdFormatUnspecified,
                            _BMDVideoInputConversionMode.bmdNoVideoInputConversion, _BMDSupportedVideoModeFlags.bmdSupportedVideoModeDefault, out _, out int supported);
                        if (supported == 0)
                            continue;

                        yield return displayMode;
                    }
                    else
                        yield break;
                }
            }
        }

        void IDeckLinkInputCallback.VideoInputFormatChanged(_BMDVideoInputFormatChangedEvents notificationEvents, IDeckLinkDisplayMode newDisplayMode, _BMDDetectedVideoInputFormatFlags detectedSignalFlags)
        {
            _BMDPixelFormat pixelFormat;

            if (!m_applyDetectedInputMode)
                return;

            var videoInputFlags = _BMDVideoInputFlags.bmdVideoInputEnableFormatDetection;

            var bmdDisplayMode = newDisplayMode.GetDisplayMode();

            if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInputRGB444))
            {
                if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInput8BitDepth))
                    pixelFormat = _BMDPixelFormat.bmdFormat8BitARGB;
                else if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInput10BitDepth))
                    pixelFormat = _BMDPixelFormat.bmdFormat10BitRGB;
                else if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInput12BitDepth))
                    pixelFormat = _BMDPixelFormat.bmdFormat12BitRGB;
                else
                    return;
            }
            else if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInputYCbCr422))
            {
                if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInput8BitDepth))
                    pixelFormat = _BMDPixelFormat.bmdFormat8BitYUV;
                else if (detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInput10BitDepth))
                    pixelFormat = _BMDPixelFormat.bmdFormat10BitYUV;
                else
                    return;
            }
            else
                return;

            var dualStream3D = detectedSignalFlags.HasFlag(_BMDDetectedVideoInputFormatFlags.bmdDetectedVideoInputDualStream3D);
            if (dualStream3D)
                videoInputFlags |= _BMDVideoInputFlags.bmdVideoInputDualStream3D;

            if (notificationEvents.HasFlag(_BMDVideoInputFormatChangedEvents.bmdVideoInputDisplayModeChanged) ||
                notificationEvents.HasFlag(_BMDVideoInputFormatChangedEvents.bmdVideoInputColorspaceChanged))
            {
                m_deckLinkInput.StopStreams();

                m_deckLinkInput.EnableVideoInput(bmdDisplayMode, pixelFormat, videoInputFlags);

                m_deckLinkInput.StartStreams();

                InputFormatChanged?.Invoke(this, new DeckLinkDeviceInputFormatEventArgs(bmdDisplayMode, dualStream3D));
            }
        }

        void IDeckLinkInputCallback.VideoInputFrameArrived(IDeckLinkVideoInputFrame videoFrame, IDeckLinkAudioInputPacket audioPacket)
        {
            if (videoFrame != null)
            {
                VideoFrameArrived?.Invoke(this, new DeckLinkDeviceInputVideoFrameEventArgs(videoFrame));

                GC.AddMemoryPressure(videoFrame.GetRowBytes() * videoFrame.GetHeight());
            }
        }

        public void StartCapture(_BMDDisplayMode displayMode, CaptureCallback captureCallback, bool applyDetectedInputMode)
        {
            if (m_currentlyCapturing)
                return;

            var videoInputFlags = _BMDVideoInputFlags.bmdVideoInputFlagDefault;

            m_applyDetectedInputMode = applyDetectedInputMode;

            if (m_supportsInputFormatDetection && m_applyDetectedInputMode)
                videoInputFlags |= _BMDVideoInputFlags.bmdVideoInputEnableFormatDetection;

            try
            {
                m_deckLinkInput.SetCallback(captureCallback);

                m_deckLinkInput.EnableVideoInput(displayMode, _BMDPixelFormat.bmdFormat8BitYUV, videoInputFlags);

                m_deckLinkInput.StartStreams();

                m_currentlyCapturing = true;
            }
            catch (Exception)
            {
                throw new DeckLinkStartCaptureException();
            }
        }

        public void StopCapture()
        {
            if (!m_currentlyCapturing)
                return;

            m_deckLinkInput.StopStreams();

            m_deckLinkInput.DisableVideoInput();

            m_deckLinkInput.SetScreenPreviewCallback(null);
            m_deckLinkInput.SetCallback(null);

            m_currentlyCapturing = false;
        }
    }

    public static class DeckLinkDeviceTools
    {
        public static string GetDisplayLabel(IDeckLink device)
        {
            device.GetDisplayName(out string displayName);
            return displayName;
        }

        public static bool IsDeviceActive(IDeckLink device)
        {
            var deckLinkAttributes = device as IDeckLinkProfileAttributes;
            deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkDuplex, out long duplexMode);
            return ((_BMDDuplexMode)duplexMode != _BMDDuplexMode.bmdDuplexInactive);
        }
    }
}
