using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Client.Messages;
using Valour.Client.Shared.Modals.ContextMenus;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.Modals.ContextMenus
{
    /// <summary>
    /// Assists in controlling the user context menu
    /// </summary>
    public class MessageContextMenu
    {
        public readonly IJSRuntime JS;
        public ClientPlanetMessage SelectedMessage;
        public MessageContextMenuComponent Component;
        public Func<Task> OpenEvent;

        public MessageContextMenu(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(MouseEventArgs e, ClientPlanetMessage message){

            SelectedMessage = message;
            Component.SetPosition(e.ClientX, e.ClientY);
            Component.SetVisibility(true);

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}
