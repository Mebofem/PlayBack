using DeckLinkAPI;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Test6
{
    class DeckLinkOutputInvalidException : Exception { }
    class DeckLinkStartPlaybackException : Exception { }

    public class DeckLinkScheduledFrameCompletedEventArgs : EventArgs
    {
        public readonly IDeckLinkVideoFrame completedFrame;
        public readonly _BMDOutputFrameCompletionResult completionResult;

        public DeckLinkScheduledFrameCompletedEventArgs(IDeckLinkVideoFrame completedFrame, _BMDOutputFrameCompletionResult completionResult)
        {
            this.completedFrame = completedFrame;
            this.completionResult = completionResult;
        }
    }

    public class DeckLinkOutputDevice : IDeckLinkVideoOutputCallback
    {
        const uint kMinimumVideoPrerollSize = 2;  

        enum PlaybackState { Idle, Prerolling, Running };

        private readonly IDeckLink m_deckLink;
        private readonly IDeckLinkOutput m_deckLinkOutput;
        private readonly IDeckLinkConfiguration m_deckLinkConfiguration;
        private readonly IDeckLinkProfileManager m_deckLinkProfileManager;

        private readonly string m_displayName;
        private readonly long m_availableOutputConnections;
        private readonly int m_supportsColorspaceMetadata;

        PlaybackState m_state;
        private readonly object m_lockState;
        private EventWaitHandle m_stopScheduledPlaybackEvent;

        public event EventHandler<DeckLinkScheduledFrameCompletedEventArgs> ScheduledFrameCompleted;

        public DeckLinkOutputDevice(IDeckLink deckLink, IDeckLinkProfileCallback profileCallback)
        {
            m_deckLink = deckLink;

            var deckLinkAttributes = m_deckLink as IDeckLinkProfileAttributes;
            deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkVideoIOSupport, out long ioSupportAttribute);
            if (!((_BMDVideoIOSupport)ioSupportAttribute).HasFlag(_BMDVideoIOSupport.bmdDeviceSupportsPlayback))
                throw new DeckLinkOutputInvalidException();

            deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkVideoOutputConnections, out m_availableOutputConnections);

            deckLinkAttributes.GetFlag(_BMDDeckLinkAttributeID.BMDDeckLinkSupportsColorspaceMetadata, out m_supportsColorspaceMetadata);

            m_deckLinkOutput = m_deckLink as IDeckLinkOutput;

            m_deckLinkConfiguration = m_deckLink as IDeckLinkConfiguration;

            m_displayName = DeckLinkDeviceTools.GetDisplayLabel(m_deckLink);

            m_deckLinkProfileManager = m_deckLink as IDeckLinkProfileManager;
            if (m_deckLinkProfileManager != null)
            {
                m_deckLinkProfileManager.SetCallback(profileCallback);
            }

            m_lockState = new object();
            m_state = PlaybackState.Idle;

            m_stopScheduledPlaybackEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        ~DeckLinkOutputDevice()
        {
            if (m_deckLinkProfileManager != null)
            {
                m_deckLinkProfileManager.SetCallback(null);
            }
        }

        public IDeckLink DeckLink => m_deckLink;
        public IDeckLinkOutput DeckLinkOutput => m_deckLinkOutput;
        public string DisplayName => m_displayName;
        public bool IsActive => DeckLinkDeviceTools.IsDeviceActive(m_deckLink);
        public bool SupportsColorspaceMetadata => Convert.ToBoolean(m_supportsColorspaceMetadata);

        public bool IsRunning
        {
            get
            {
                lock (m_lockState)
                {
                    return m_state != PlaybackState.Idle;
                }
            }
        }

        public _BMDLinkConfiguration CurrentLinkConfiguration
        {
            get
            {
                m_deckLinkConfiguration.GetInt(_BMDDeckLinkConfigurationID.bmdDeckLinkConfigSDIOutputLinkConfiguration, out long currentLinkConfiguration);
                return (_BMDLinkConfiguration)currentLinkConfiguration;
            }
            set
            {
                m_deckLinkConfiguration.SetInt(_BMDDeckLinkConfigurationID.bmdDeckLinkConfigSDIOutputLinkConfiguration, (long)value);
            }
        }

        public bool HasSDIOutputConnection
        {
            get
            {
                var outputConnections = (_BMDVideoConnection)m_availableOutputConnections;
                return outputConnections.HasFlag(_BMDVideoConnection.bmdVideoConnectionSDI) || outputConnections.HasFlag(_BMDVideoConnection.bmdVideoConnectionOpticalSDI);
            }
        }

        public bool IsLinkConfigurationSupported(_BMDLinkConfiguration configuration)
        {
            var deckLinkAttributes = m_deckLink as IDeckLinkProfileAttributes;

            switch (configuration)
            {
                case _BMDLinkConfiguration.bmdLinkConfigurationSingleLink:
                    return true;

                case _BMDLinkConfiguration.bmdLinkConfigurationDualLink:
                    deckLinkAttributes.GetFlag(_BMDDeckLinkAttributeID.BMDDeckLinkSupportsDualLinkSDI, out int supportsDualLink);
                    return Convert.ToBoolean(supportsDualLink);

                case _BMDLinkConfiguration.bmdLinkConfigurationQuadLink:
                    deckLinkAttributes.GetFlag(_BMDDeckLinkAttributeID.BMDDeckLinkSupportsQuadLinkSDI, out int supportsQuadLink);
                    return Convert.ToBoolean(supportsQuadLink);

                default:
                    return false;
            }
        }

        public void PrepareForPlayback(_BMDDisplayMode displayMode, PlaybackCallback playbackCallback)
        {
            m_deckLinkOutput.SetScheduledFrameCompletionCallback(playbackCallback);

            m_deckLinkOutput.EnableVideoOutput(displayMode, _BMDVideoOutputFlags.bmdVideoOutputFlagDefault);
        }

        public void ScheduleFrame(Mat frame)
        {
            try
            {
                int frameWidth = frame.Width;
                int frameHeight = frame.Height;

                int bytesPerPixel = 2;
                int frameByteSize = frameWidth * frameHeight * bytesPerPixel;

                byte[] frameData = new byte[frameByteSize];

                Marshal.Copy(frame.Data, frameData, 0, frameByteSize);

                m_deckLinkOutput.CreateVideoFrame(frameWidth, frameHeight, frameWidth * bytesPerPixel, _BMDPixelFormat.bmdFormat8BitYUV, _BMDFrameFlags.bmdFrameFlagDefault, out IDeckLinkMutableVideoFrame deckLinkFrame);

                deckLinkFrame.GetBytes(out IntPtr deckLinkFrameBuffer);

                Marshal.Copy(frameData, 0, deckLinkFrameBuffer, frameByteSize);

                m_deckLinkOutput.ScheduleVideoFrame(deckLinkFrame, 0, frameByteSize, 1001);

                m_deckLinkOutput.StartScheduledPlayback(0, 100, 1.0);

                lock (m_lockState)
                {
                    m_state = PlaybackState.Running;
                }
            }
            catch (COMException comEx)
            {
                
            }
        }

        public bool IsDisplayModeSupported(_BMDDisplayMode displayMode, _BMDPixelFormat pixelFormat)
        {
            _BMDVideoConnection videoConnection = HasSDIOutputConnection ? _BMDVideoConnection.bmdVideoConnectionSDI : _BMDVideoConnection.bmdVideoConnectionUnspecified;

            _BMDSupportedVideoModeFlags supportedFlags = DeckLinkDeviceToolsOutput.GetSDIConfigurationVideoModeFlags(HasSDIOutputConnection, CurrentLinkConfiguration);

            m_deckLinkOutput.DoesSupportVideoMode(videoConnection, displayMode, pixelFormat, _BMDVideoOutputConversionMode.bmdNoVideoOutputConversion,
                supportedFlags, out _, out int supported);
            return Convert.ToBoolean(supported);
        }

        public bool DoesDisplayModeSupport3D(_BMDDisplayMode displayMode)
        {
            m_deckLinkOutput.GetDisplayMode(displayMode, out IDeckLinkDisplayMode deckLinkDisplayMode);
            return deckLinkDisplayMode.GetFlags().HasFlag(_BMDDisplayModeFlags.bmdDisplayModeSupports3D);
        }

        public bool IsColorspaceSupported(_BMDDisplayMode displayMode, _BMDColorspace colorspace)
        {
            m_deckLinkOutput.GetDisplayMode(displayMode, out IDeckLinkDisplayMode deckLinkDisplayMode);
            var displayHeight = deckLinkDisplayMode.GetHeight();

            switch (colorspace)
            {
                case _BMDColorspace.bmdColorspaceRec601:
                    return displayHeight < 720;

                case _BMDColorspace.bmdColorspaceRec709:
                    return displayHeight >= 720;
            }

            return false;
        }

        public bool SupportsHFRTimecode
        {
            get
            {
                var deckLinkAttributes = m_deckLink as IDeckLinkProfileAttributes;
                deckLinkAttributes.GetFlag(_BMDDeckLinkAttributeID.BMDDeckLinkSupportsHighFrameRateTimecode, out int supportsHFRTC);
                return Convert.ToBoolean(supportsHFRTC);
            }
        }

        public IEnumerable<IDeckLinkDisplayMode> DisplayModes
        {
            get
            {
                _BMDSupportedVideoModeFlags supportedFlags = DeckLinkDeviceToolsOutput.GetSDIConfigurationVideoModeFlags(HasSDIOutputConnection, CurrentLinkConfiguration);
                _BMDVideoConnection videoConnection = HasSDIOutputConnection ? _BMDVideoConnection.bmdVideoConnectionSDI : _BMDVideoConnection.bmdVideoConnectionUnspecified;

                m_deckLinkOutput.GetDisplayModeIterator(out IDeckLinkDisplayModeIterator displayModeIterator);
                if (displayModeIterator == null)
                    yield break;

                while (true)
                {
                    displayModeIterator.Next(out IDeckLinkDisplayMode displayMode);

                    if (displayMode != null)
                    {
                        m_deckLinkOutput.DoesSupportVideoMode(videoConnection, displayMode.GetDisplayMode(), _BMDPixelFormat.bmdFormatUnspecified,
                            _BMDVideoOutputConversionMode.bmdNoVideoOutputConversion, supportedFlags, out _, out int supported);
                        if (!Convert.ToBoolean(supported))
                            continue;

                        yield return displayMode;
                    }
                    else
                        yield break;
                }
            }
        }

        public uint VideoWaterLevel
        {
            get
            {
                var deckLinkAttributes = m_deckLink as IDeckLinkProfileAttributes;
                deckLinkAttributes.GetInt(_BMDDeckLinkAttributeID.BMDDeckLinkMinimumPrerollFrames, out long minimumPrerollFrames);
                return Math.Max((uint)minimumPrerollFrames, kMinimumVideoPrerollSize);
            }
        }
        public void StopPlayback()
        {
            PlaybackState state;
            lock (m_lockState)
            {
                state = m_state;
            }
            if (state == PlaybackState.Running)
            {
                m_deckLinkOutput.StopScheduledPlayback(0, out _, 100);
                m_stopScheduledPlaybackEvent.WaitOne();
                m_deckLinkOutput.SetScheduledFrameCompletionCallback(null);
                m_deckLinkOutput.DisableVideoOutput();
                lock (m_lockState)
                {
                    m_state = PlaybackState.Idle;
                }
            }
        }

        #region callbacks
        void IDeckLinkVideoOutputCallback.ScheduledFrameCompleted(IDeckLinkVideoFrame completedFrame, _BMDOutputFrameCompletionResult result)
        {
            ScheduledFrameCompleted?.Invoke(this, new DeckLinkScheduledFrameCompletedEventArgs(completedFrame, result));
        }

        void IDeckLinkVideoOutputCallback.ScheduledPlaybackHasStopped()
        {
            m_stopScheduledPlaybackEvent.Set();
        }
        #endregion
    }

    public static class DeckLinkDeviceToolsOutput
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

        public static _BMDSupportedVideoModeFlags GetSDIConfigurationVideoModeFlags(bool supportsSDIOutput, _BMDLinkConfiguration linkConfiguration = _BMDLinkConfiguration.bmdLinkConfigurationSingleLink)
        {
            _BMDSupportedVideoModeFlags flags = _BMDSupportedVideoModeFlags.bmdSupportedVideoModeDefault;

            if (supportsSDIOutput)
            {
                switch (linkConfiguration)
                {
                    case _BMDLinkConfiguration.bmdLinkConfigurationSingleLink:
                        flags = _BMDSupportedVideoModeFlags.bmdSupportedVideoModeSDISingleLink;
                        break;

                    case _BMDLinkConfiguration.bmdLinkConfigurationDualLink:
                        flags = _BMDSupportedVideoModeFlags.bmdSupportedVideoModeSDIDualLink;
                        break;

                    case _BMDLinkConfiguration.bmdLinkConfigurationQuadLink:
                        flags = _BMDSupportedVideoModeFlags.bmdSupportedVideoModeSDIQuadLink;
                        break;
                }
            }
            return flags;
        }

        public static int BytesPerRow(_BMDPixelFormat pixelFormat, int frameWidth)
        {
            int bytesPerRow;

            switch (pixelFormat)
            {
                case _BMDPixelFormat.bmdFormat8BitYUV:
                    bytesPerRow = frameWidth * 2;
                    break;

                case _BMDPixelFormat.bmdFormat10BitYUV:
                    bytesPerRow = ((frameWidth + 47) / 48) * 128;
                    break;

                case _BMDPixelFormat.bmdFormat10BitRGB:
                    bytesPerRow = ((frameWidth + 63) / 64) * 256;
                    break;

                case _BMDPixelFormat.bmdFormat8BitARGB:
                case _BMDPixelFormat.bmdFormat8BitBGRA:
                default:
                    bytesPerRow = frameWidth * 4;
                    break;
            }

            return bytesPerRow;
        }
    }
}
