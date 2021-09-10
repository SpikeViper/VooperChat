﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared.Planets
{
    public class Planet
    {
        /// <summary>
        /// The ID of the planet
        /// </summary>
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The Id of the owner of this planet
        /// </summary>
        [JsonPropertyName("Owner_Id")]
        public ulong Owner_Id { get; set; }

        /// <summary>
        /// The name of the planet
        /// </summary>
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        /// <summary>
        /// The image url for the planet 
        /// </summary>
        [JsonPropertyName("Image_Url")]
        public string Image_Url { get; set; }

        /// <summary>
        /// The description of the planet
        /// </summary>
        [JsonPropertyName("Description")]
        public string? Description { get; set; }

        /// <summary>
        /// If the server requires express allowal to join a planet
        /// </summary>
        [JsonPropertyName("Public")]
        public bool Public { get; set; }

        /// <summary>
        /// The amount of members on the planet
        /// </summary>
        [JsonPropertyName("Member_Count")]
        public uint Member_Count { get; set;}

        /// <summary>
        /// The default role for the planet
        /// </summary>
        [JsonPropertyName("Default_Role_Id")]
        public ulong Default_Role_Id { get; set; }

        /// <summary>
        /// The id of the main channel of the planet
        /// </summary>
        [JsonPropertyName("Main_Channel_Id")]
        public ulong Main_Channel_Id { get; set; }
    }
}
