using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TomTom.Tools
{
	public class POI {
		/// <summary>
		/// The Name of the POI
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Any extended data to add to the POI
		/// </summary>
		public string ExtendedData { get; set; }

		/// <summary>
		/// The Latitude in WGS84
		/// </summary>
		public double Lat { get; set; }

		/// <summary>
		/// The Longitude in WGS84
		/// </summary>
		public double Long { get; set; }

		/// <summary>
		/// The telephone number of the POI
		/// </summary>
		public string Telephone { get; set; }
	}
}
