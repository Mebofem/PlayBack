using DeckLinkAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Test6
{
    public class DeckLinkProfileEventArgs : EventArgs
    {
        public readonly IDeckLinkProfile deckLinkProfile;

        public DeckLinkProfileEventArgs(IDeckLinkProfile deckLinkProfile) => this.deckLinkProfile = deckLinkProfile;
    }

    class ProfileCallback : IDeckLinkProfileCallback
    {
        public event EventHandler<DeckLinkProfileEventArgs> ProfileChanging;
        public event EventHandler<DeckLinkProfileEventArgs> ProfileActivated;

        public ProfileCallback()
        {
        }

        void IDeckLinkProfileCallback.ProfileChanging(IDeckLinkProfile profileToBeActivated, int streamsWillBeForcedToStop)
        {
            if (Convert.ToBoolean(streamsWillBeForcedToStop))
            {
                ProfileChanging?.Invoke(this, new DeckLinkProfileEventArgs(profileToBeActivated));
            }
        }

        void IDeckLinkProfileCallback.ProfileActivated(IDeckLinkProfile activatedProfile)
        {
            ProfileActivated?.Invoke(this, new DeckLinkProfileEventArgs(activatedProfile));
        }
    }
}
