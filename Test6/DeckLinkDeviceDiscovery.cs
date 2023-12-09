using DeckLinkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test6
{
    public class DeckLinkDiscoveryEventArgs : EventArgs
    {
        public readonly IDeckLink deckLink;

        public DeckLinkDiscoveryEventArgs(IDeckLink deckLink)
        {
            this.deckLink = deckLink;
        }
    }

    public class DeckLinkDeviceDiscovery : IDeckLinkDeviceNotificationCallback
    {
        private IDeckLinkDiscovery m_deckLinkDiscovery;
        private bool m_deckLinkDiscoveryEnabled = false;

        public event EventHandler<DeckLinkDiscoveryEventArgs> DeviceArrived;
        public event EventHandler<DeckLinkDiscoveryEventArgs> DeviceRemoved;

        public DeckLinkDeviceDiscovery()
        {
            m_deckLinkDiscovery = new CDeckLinkDiscovery();
        }
        ~DeckLinkDeviceDiscovery()
        {
            Disable();
        }

        public void Enable()
        {
            m_deckLinkDiscovery.InstallDeviceNotifications(this);
            m_deckLinkDiscoveryEnabled = true;
        }

        public void Disable()
        {
            if (m_deckLinkDiscoveryEnabled)
            {
                m_deckLinkDiscovery.UninstallDeviceNotifications();
                m_deckLinkDiscoveryEnabled = false;
            }
        }

        #region callbacks
        void IDeckLinkDeviceNotificationCallback.DeckLinkDeviceArrived(IDeckLink deckLinkDevice)
        {
            DeviceArrived?.Invoke(this, new DeckLinkDiscoveryEventArgs(deckLinkDevice));
        }

        void IDeckLinkDeviceNotificationCallback.DeckLinkDeviceRemoved(IDeckLink deckLinkDevice)
        {
            DeviceRemoved?.Invoke(this, new DeckLinkDiscoveryEventArgs(deckLinkDevice));
        }
        #endregion
    }
}
