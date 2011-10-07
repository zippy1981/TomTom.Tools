using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;

namespace TomTom.Tools
{
	#region Helper Classes
	internal enum POIRecordTypes {
		Deleted = 0x00,
		Skipper = 0x01,
		SimplePOI = 0x02,
		ExtendedPOI = 0x03
	}
	
	public class GeoSize {
		private double width = 0;
		public double Width {
			get { return width; }
			set { width = value; }
		}
		
		private double height = 0;
		public double Height {
			get { return height; }
			set { height = value; }
		}
		
		public GeoSize() {}
		public GeoSize(double width, double height) {
			this.width = width;
			this.height = height;
		}
	}
	
	internal class GeoPoint {
		public double x = 0;
		public double y = 0;
		
		public Int32 TomTomXPos {
			get { return (Int32)(x * 100000); }
		}

		public Int32 TomTomYPos {
			get { return (Int32)(y * 100000); }
		}
		
		public GeoPoint() {}
		public GeoPoint(double xPos, double yPos) {
			x = xPos;
			y = yPos;
		}
	}
	
	internal class GeoRect {
		public GeoPoint BottomLeft = new GeoPoint();
		public GeoPoint TopRight = new GeoPoint();
		public List<POI> POIs = new List<POI>();
		
		public GeoRect() {}
		public GeoRect(GeoPoint bottomLeft, GeoPoint topRight) {
			BottomLeft = bottomLeft;
			TopRight = topRight;
		}
	}
	#endregion
	
	public class OV2Parser
	{
		#region Properties
		private byte SignedBit = 0x80;
		
		/// <summary>
		/// Size of Simple POI binary structure.  Including null terminating string characters.
		/// </summary>
		private int SimplePOIStructSize = 14;

		/// <summary>
		/// Size of Extended POI binary structure.  Including null terminating string characters.
		/// </summary>
		private int ExendedPOIStructSize = 16;

		/// <summary>
		/// Size of Skipper Record binary structure.
		/// </summary>
		private int SkipperStructSize = 21;

		/// <summary>
		/// Size of Deleted Record binary structure.
		/// </summary>
		private int DeletedStructSize = 10;
		
		private GeoSize minRectSize = new GeoSize(0.00113, 0.00139);
		/// <summary>
		/// The minimum size for a rectangle containing POIs.  This is required in the case there are more than 'MaxPOIsInRectangle' at exactly the same Lat/Long.
		/// </summary>
		public GeoSize MinRectSize {
			get { return minRectSize; }
			set { minRectSize = value; }
		}
		
		private int maxPOIsInRectangle = 16;
		/// <summary>
		/// The maximum amount of POIs in bounding rectangles
		/// </summary>
		public int MaxPOIsInRectangle {
			get { return maxPOIsInRectangle; }
			set { maxPOIsInRectangle = value; }
		}
		#endregion
		
		/// <summary>
		/// Creates an OV2 file.  If the file exists it will be overwritten
		/// </summary>
		/// <param name="FilePath">The fille path and filename for the OV2 file</param>
		/// <param name="POIs">The list of POIs to write to the OV2 file</param>
		public void CreateOV2(string FilePath, List<POI> POIs)
		{
			BinaryWriter Writer = new BinaryWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None));
			
			//Calculate rectangles for all POIs
			GeoRect BoundingRect = CalculateBoundingRect(POIs);
			List<GeoRect> Rectangles = ProcessPOIs(BoundingRect);
			
			//Write the OV2 header
			Writer.Write(GenerateOV2Header(BoundingRect, Rectangles, POIs));
			
			//Write everything!
			foreach(GeoRect CurrentRect in Rectangles) {
				//Write the current rectangle
				Writer.Write(SerialiseRectangle(CurrentRect));
				
				//Write all POIs
				foreach(POI CurrentPOI in CurrentRect.POIs) {
					if (String.IsNullOrEmpty(CurrentPOI.ExtendedData)) {
						Writer.Write(SerialiseSimplePOI(CurrentPOI));
					}
					else {
						Writer.Write(SerialiseExtendedPOI(CurrentPOI));
					}
				}
			}
			
			//We are finished, flush and close
			Writer.Flush();
			Writer.Close();
		}
		
		/// <summary>
		/// OPens an OV2 file and reads all POIs into a POI List
		/// </summary>
		/// <param name="FilePath">The path to the OV2 file</param>
		/// <returns>The list of all POIs in the OV2</returns>
		public List<POI> ReadOV2(string FilePath) {
			bool bReading = true;
			int Pos = SkipperStructSize;
			byte[] BinInt32 = new byte[4];
			Int32 BlockSize = 0;
			List<POI> POIs = new List<POI>();
			BinaryReader Reader = new BinaryReader(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None));
			
			//Read all bytes from the file
			byte[] ReadBytes = new byte[Reader.BaseStream.Length];
			Reader.Read(ReadBytes, 0, (int)Reader.BaseStream.Length);
			
			//Read until end of file
			while(bReading) {
				POIRecordTypes RecType = (POIRecordTypes)ReadBytes[Pos];
				switch(RecType) {
					case POIRecordTypes.Skipper: {
						Pos += SkipperStructSize;
						break;
					}
					case POIRecordTypes.SimplePOI: {
						BinInt32[0] = ReadBytes[Pos + 1];
						BinInt32[1] = ReadBytes[Pos + 2];
						BinInt32[2] = ReadBytes[Pos + 3];
						BinInt32[3] = ReadBytes[Pos + 4];
						BlockSize = BitConverter.ToInt32(BinInt32, 0);

						//Read this POI block
						byte[] POIBlock = new byte[BlockSize];
						Array.Copy(ReadBytes, Pos, POIBlock, 0, BlockSize);

						//Process POI block
						POI NewPOI = ConvertToPOI(POIBlock);
						POIs.Add(NewPOI);
						
						//Increment Pos
						Pos += SimplePOIStructSize + NewPOI.Name.Length;

						//Include length of telephone number, if there was one!
						if(!(String.IsNullOrEmpty(NewPOI.Telephone))) {
							Pos += NewPOI.Telephone.Length + 1;
						}
						break;
					}
					case POIRecordTypes.ExtendedPOI: {
						BinInt32[0] = ReadBytes[Pos + 1];
						BinInt32[1] = ReadBytes[Pos + 2];
						BinInt32[2] = ReadBytes[Pos + 3];
						BinInt32[3] = ReadBytes[Pos + 4];
						BlockSize = BitConverter.ToInt32(BinInt32, 0);

						//Read this POI block
						byte[] POIBlock = new byte[BlockSize];
						Array.Copy(ReadBytes, Pos, POIBlock, 0, BlockSize);

						//Process POI block
						POI NewPOI = ConvertToPOI(POIBlock);
						POIs.Add(NewPOI);

						//Increment Pos
						Pos += ExendedPOIStructSize + NewPOI.Name.Length + NewPOI.ExtendedData.Length;
						
						//Include length of telephone number, if there was one!
						if (!(String.IsNullOrEmpty(NewPOI.Telephone))) {
							Pos += NewPOI.Telephone.Length + 1;
						}
						break;
					}
					case POIRecordTypes.Deleted: {
						Pos += DeletedStructSize;
						break;
					}
					default: {
						throw new ApplicationException("Invalid byte read, file maybe corrupt");
					}
				}
				
				//Are we still reading?
				bReading = Pos < ReadBytes.Length;
			}
			
			//We are finished, so close
			Reader.Close();
			return POIs;
		}
		
		private POI ConvertToPOI(byte[] POIData) {
			POI ReturnPOI = new POI();
			int Pos = 0;
			byte[] BinInt32 = new byte[4];
			byte ReadByte = 0x01;
			
			//Get longitude
			BinInt32[Pos] = POIData[Pos + 5];
			BinInt32[Pos + 1] = POIData[Pos + 6];
			BinInt32[Pos + 2] = POIData[Pos + 7];
			BinInt32[Pos + 3] = POIData[Pos + 8];
			ReturnPOI.Long = (double)BitConverter.ToInt32(BinInt32, 0) / (double)100000;

			//Get latitude
			BinInt32[Pos] = POIData[Pos + 9];
			BinInt32[Pos + 1] = POIData[Pos + 10];
			BinInt32[Pos + 2] = POIData[Pos + 11];
			BinInt32[Pos + 3] = POIData[Pos + 12];
			ReturnPOI.Lat = (double)BitConverter.ToInt32(BinInt32, 0) / (double)100000;
			
			//Get Name
			Pos = Pos + 13;
			ReadByte = POIData[Pos];
			while(ReadByte != 0x00) {
				ReturnPOI.Name += (char)ReadByte;
				Pos++;
				ReadByte = POIData[Pos];
			}
			
			//DOes the name contain a phone number?
			if(ReturnPOI.Name.Contains('>')) {
				int GTPos = ReturnPOI.Name.LastIndexOf('>');
				ReturnPOI.Telephone = ReturnPOI.Name.Substring(GTPos + 1);
				ReturnPOI.Name = ReturnPOI.Name.Substring(0, GTPos);
			}
			
			//If this is an extended POI, then read the extended data
			if(POIData[0] == (byte)POIRecordTypes.ExtendedPOI) {
				Pos++;
				ReadByte = POIData[Pos];
				while (ReadByte != 0x00) {
					ReturnPOI.ExtendedData += (char)ReadByte;
					Pos++;
					ReadByte = POIData[Pos];
				}
			}
			
			return ReturnPOI;
		}
		
		#region Rectangle & POI processing
		/// <summary>
		/// Creates a list of rectangles with a maximum of 'MaxPOIsInRectangle' POIs in each rectangle
		/// </summary>
		/// <param name="Rectangles">The rectangles to process</param>
		/// <returns>A list containing the processed rectangles</returns>
		private List<GeoRect> ProcessPOIs(GeoRect BoundingRectangle) {
			List<GeoRect> lReturn = new List<GeoRect>();
			bool bSplitRect = true;
			int i = 0;
			GeoRect CurrentRect = null;
			List<GeoRect> RectsToProcess = new List<GeoRect>();
			List<GeoRect> ProcessedRects = null;
			
			//Initialise rectangles to process with full bounding rectangle
			RectsToProcess.Add(BoundingRectangle);
			
			//Keep looping until we do not split anything! - Replaced recurrsive call with loop due to stack overflows with large areas!
			while(bSplitRect) {
				bSplitRect = false;
				ProcessedRects = new List<GeoRect>();
				
				for(i = 0; i < RectsToProcess.Count; i++) {
					CurrentRect = RectsToProcess[i];
					if(CurrentRect.POIs.Count > maxPOIsInRectangle && !RectangleIsMinSize(CurrentRect)) {
						//Split the rectangle into 2
						List<GeoRect> SplitRectangles = SplitRectangle(CurrentRect);

						//Determine which POIs reside in which of the split rectangles
						DeterminePOIsInSplitRectangles(SplitRectangles, CurrentRect.POIs);
						
						//Add our 2 new rectangles for further processing
						ProcessedRects.AddRange(SplitRectangles);
						
						//We have split the rectangle!
						bSplitRect = true;
					}
					else if (CurrentRect.POIs.Count != 0) {
						lReturn.Add(CurrentRect);
					}
				}
				
				RectsToProcess = new List<GeoRect>(ProcessedRects);
			}
			
			return lReturn;
		}
		
		/// <summary>
		/// Determins if the rectangle is equal to or smaller than the minimum size of a POI bounding rectangle
		/// </summary>
		/// <param name="Rectangle">The Rectangle</param>
		/// <returns>True if the rectangle is equal to or smaller than the minimum size for a POI bounding rectangle</returns>
		private bool RectangleIsMinSize(GeoRect Rectangle) {
			bool bReturn = false;
			
			if(	GeoDifference(Rectangle.BottomLeft.x, Rectangle.TopRight.x) <= minRectSize.Width &&
				GeoDifference(Rectangle.BottomLeft.y, Rectangle.TopRight.y) <= minRectSize.Height) {
					bReturn = true;
			}
			
			return bReturn;
		}
		
		/// <summary>
		/// Determines which POIs belong in which rectangles
		/// </summary>
		/// <param name="Rectangles">The rectangle list created by calling SplitRectangle()</param>
		/// <param name="POIs">The List of POIs to porcess and place into each rectangle</param>
		private void DeterminePOIsInSplitRectangles(List<GeoRect> Rectangles, List<POI> POIs) {
			GeoRect Rect1 = Rectangles[0];
			GeoRect Rect2 = Rectangles[1];
			
			foreach(POI CurrentPOI in POIs) {
				//Is the POI in rectangle 1?
				if(	CurrentPOI.Long >= Rect1.BottomLeft.x && 
					CurrentPOI.Long < Rect1.TopRight.x &&
					CurrentPOI.Lat >= Rect1.BottomLeft.y &&
					CurrentPOI.Lat < Rect1.TopRight.y) {
					
					Rect1.POIs.Add(CurrentPOI);
				}
				//Its in rectangle 2
				else {
					Rect2.POIs.Add(CurrentPOI);
				}
			}
		}
		
		/// <summary>
		/// Returns the byte size of a POI if it was serialised
		/// </summary>
		/// <param name="poi">The POI</param>
		/// <returns>The byte size of the POI if serialised</returns>
		private Int32 GetPOISize(POI poi) {
			Int32 iReturn = 0;
			if(String.IsNullOrEmpty(poi.ExtendedData)) {
				iReturn += SimplePOIStructSize + poi.Name.Length;
			}
			else {
				iReturn += ExendedPOIStructSize + poi.Name.Length + poi.ExtendedData.Length;
			}
			
			if(!String.IsNullOrEmpty(poi.Telephone)) {
				iReturn += poi.Telephone.Length + 1;
			}
			return iReturn;
		}
		
		/// <summary>
		/// Splits a rectangle along its longest side
		/// </summary>
		/// <param name="Rectangle">The ectangle to split</param>
		/// <returns>A list containing the split rectangles</returns>
		private List<GeoRect> SplitRectangle(GeoRect Rectangle) {
			List<GeoRect> lReturn = new List<GeoRect>();
			
			double NorthSouthDistance = GeoDifference(Rectangle.BottomLeft.y, Rectangle.TopRight.y);
			double WestEastDifference = GeoDifference(Rectangle.BottomLeft.x, Rectangle.TopRight.x);

			//Is the North to South line the longest?
			if (NorthSouthDistance >= WestEastDifference) {
				//Split rectangle along North-South
				GeoRect TopRect = new GeoRect(	new GeoPoint(Rectangle.BottomLeft.x, Rectangle.BottomLeft.y + (NorthSouthDistance / 2)),
												new GeoPoint(Rectangle.TopRight.x, Rectangle.TopRight.y)
											  );
				GeoRect BottomRect = new GeoRect(	new GeoPoint(Rectangle.BottomLeft.x, Rectangle.BottomLeft.y),
													new GeoPoint(Rectangle.TopRight.x, Rectangle.TopRight.y - (NorthSouthDistance / 2))
												);
				
				//Add new rectangles to array
				lReturn.Add(TopRect);
				lReturn.Add(BottomRect);

			}
			//The West to East line is longest
			else {
				//Split rectangle along West-East
				GeoRect LeftRect = new GeoRect(	new GeoPoint(Rectangle.BottomLeft.x, Rectangle.BottomLeft.y),
												new GeoPoint(Rectangle.TopRight.x - (WestEastDifference / 2), Rectangle.TopRight.y)
											  );
				GeoRect RightRect = new GeoRect(	new GeoPoint(Rectangle.BottomLeft.x + (WestEastDifference / 2), Rectangle.BottomLeft.y),
													new GeoPoint(Rectangle.TopRight.x, Rectangle.TopRight.y)
												);
				
				//Add new rectangles to array
				lReturn.Add(LeftRect);
				lReturn.Add(RightRect);
			}
			
			return lReturn;
		}
		
		/// <summary>
		/// Calculates the difference between 2 numbers
		/// </summary>
		/// <param name="Pt1">The first number</param>
		/// <param name="Pt2">The second number</param>
		/// <returns>The difference, always positive</returns>
		private double GeoDifference(double Pt1, double Pt2) {
			double dReturn = 0;
			
			if(Pt1 <= 0 && Pt2 <= 0) {
				dReturn = Pt1 - Pt2;
			}
			else if(Pt1 >= 0 && Pt2 <= 0) {
				dReturn = Pt1 + (-Pt2);
			}
			else if(Pt1 <= 0 && Pt2 >= 0) {
				dReturn = (-Pt1) + Pt2;
			}
			else if(Pt1 >= 0 && Pt2 >= 0) {
				dReturn = Pt1 - Pt2;
			}
			
			//Ensure the return value is always positive difference
			if(dReturn < 0) {
				dReturn = -dReturn;
			}
			
			return dReturn;
		}
		
		/// <summary>
		/// Calculates the full dounding rectangle around a list of POIs
		/// </summary>
		/// <param name="POIs">The list of POIs</param>
		/// <returns>The bounding rectangle</returns>
		private GeoRect CalculateBoundingRect(List<POI> POIs) {
			GeoRect ReturnRect = new GeoRect();
			
			//Initialise rect with max/min values
			ReturnRect.BottomLeft.x = 90;	//West co-ordinates are negative, so set to max value
			ReturnRect.BottomLeft.y = 180;	//South co-ordinates are negative, so set to max value
			ReturnRect.TopRight.x = -90;	//East co-ordinates are positive, so set to min value
			ReturnRect.TopRight.y = -180;	//North co-ordinates are positive, so set to min value
			
			//For all POIs
			foreach(POI CurrentPOI in POIs) {
				//Calculate bounding rectangle for all POIs
				if (CurrentPOI.Long < ReturnRect.BottomLeft.x) {
					ReturnRect.BottomLeft.x = CurrentPOI.Long;
				}
				if (CurrentPOI.Long > ReturnRect.TopRight.x) {
					ReturnRect.TopRight.x = CurrentPOI.Long;
				}
				if (CurrentPOI.Lat < ReturnRect.BottomLeft.y) {
					ReturnRect.BottomLeft.y = CurrentPOI.Lat;
				}
				if (CurrentPOI.Lat > ReturnRect.TopRight.y) {
					ReturnRect.TopRight.y = CurrentPOI.Lat;
				}
			}
			
			//All POIs are in this rectangle
			ReturnRect.POIs = POIs;
			
			return ReturnRect;
		}
		#endregion
		
		#region Serialisation
		/// <summary>
		/// Creates the OV2 file header
		/// </summary>
		/// <param name="BoundingRect">The full bounding rectangle for all POIs within the OV2</param>
		/// <param name="Rectangles">List containing all rectangles and POIs contained in the OV2</param>
		/// <param name="AllPOIs">A list of all POIs within all the calculated rectangles</param>
		/// <returns>The byte array file header</returns>
		private byte[] GenerateOV2Header(GeoRect BoundingRect, List<GeoRect> Rectangles, List<POI> AllPOIs) {
			byte[] bReturn = new byte[SkipperStructSize];
			Int32 Size = SkipperStructSize;

			//Calculate total size for every POI in the OV2
			foreach (POI CurrentPOI in AllPOIs) {
				Size += GetPOISize(CurrentPOI);
			}

			//Calculate total size of all skipper records in teh database
			Size += (Rectangles.Count * SkipperStructSize);

			//Serialise data
			bReturn[0] = 0x01;
			ConvertToLEBytes(Size).CopyTo(bReturn, 1);
			ConvertToLEBytes((Int32)(BoundingRect.TopRight.TomTomXPos)).CopyTo(bReturn, 5);
			ConvertToLEBytes((Int32)(BoundingRect.TopRight.TomTomYPos)).CopyTo(bReturn, 9);
			ConvertToLEBytes((Int32)(BoundingRect.BottomLeft.TomTomXPos)).CopyTo(bReturn, 13);
			ConvertToLEBytes((Int32)(BoundingRect.BottomLeft.TomTomYPos)).CopyTo(bReturn, 17);

			return bReturn;
		}
		
		/// <summary>
		/// Serialises a Rectangle into a TomTom skipper record
		/// </summary>
		/// <param name="Rectangle">The rectangle to serialise</param>
		/// <returns>The serialised rectangle</returns>
		private byte[] SerialiseRectangle(GeoRect Rectangle) {
			byte[] bReturn = new byte[SkipperStructSize];
			Int32 AllPOIsSize = SkipperStructSize;
			
			//Calculate Size of POIs
			foreach(POI CurrentPOI in Rectangle.POIs) {
				AllPOIsSize += GetPOISize(CurrentPOI);
			}
			
			//Serialise
			bReturn[0] = 0x01;
			ConvertToLEBytes(AllPOIsSize).CopyTo(bReturn, 1);
			ConvertToLEBytes((Int32)(Rectangle.TopRight.TomTomXPos)).CopyTo(bReturn, 5);
			ConvertToLEBytes((Int32)(Rectangle.TopRight.TomTomYPos)).CopyTo(bReturn, 9);
			ConvertToLEBytes((Int32)(Rectangle.BottomLeft.TomTomXPos)).CopyTo(bReturn, 13);
			ConvertToLEBytes((Int32)(Rectangle.BottomLeft.TomTomYPos)).CopyTo(bReturn, 17);

			return bReturn;
		}
		
		/// <summary>
		/// Serailises a TomTom extended POI into a byte stream
		/// </summary>
		/// <param name="Rec">The TomTom POI record to serialise</param>
		/// <returns>The serialised byte stream</returns>
		private byte[] SerialiseExtendedPOI(POI Rec)
		{
			int Pos = 0;
			int Size = ExendedPOIStructSize + Rec.Name.Length + Rec.ExtendedData.Length;
			if (!String.IsNullOrEmpty(Rec.Telephone)) {
				Size += Rec.Telephone.Length + 1;
			}
			byte[] bReturn = new byte[Size];
			bReturn[Pos] = 0x03;
			Pos++;
			ConvertToLEBytes(Size).CopyTo(bReturn, Pos);
			Pos += 4;
			ConvertToLEBytes((Int32)(Rec.Long * 100000)).CopyTo(bReturn, Pos);
			Pos += 4;
			ConvertToLEBytes((Int32)(Rec.Lat * 100000)).CopyTo(bReturn, Pos);
			Pos += 4;
			Encoding.ASCII.GetBytes(Rec.Name).CopyTo(bReturn, Pos);
			Pos += Rec.Name.Length;
			
			if (!String.IsNullOrEmpty(Rec.Telephone)) {
				bReturn[Pos] = 0x3E;
				Pos += 1;
				Encoding.ASCII.GetBytes(Rec.Telephone).CopyTo(bReturn, Pos);
				Pos += Rec.Telephone.Length;
			}
			bReturn[Pos] = 0x00;
			Pos++;
			Encoding.ASCII.GetBytes(Rec.ExtendedData).CopyTo(bReturn, Pos);
			Pos++;
			bReturn[Pos] = 0x00;
			Pos++;
			bReturn[Pos] = 0x00;
			return bReturn;
		}
		
		/// <summary>
		/// Serailises a TomTom simple POI into a byte stream
		/// </summary>
		/// <param name="Rec">The TomTom POI record to serialise</param>
		/// <returns>The serialised byte stream</returns>
		private byte[] SerialiseSimplePOI(POI Rec) {
			int Pos = 0;
			int Size = SimplePOIStructSize + Rec.Name.Length;
			
			if(!String.IsNullOrEmpty(Rec.Telephone)) {
				Size += Rec.Telephone.Length + 1;
			}
			
			byte[] bReturn = new byte[Size];
			bReturn[Pos] = 0x02;
			Pos++;
			ConvertToLEBytes(Size).CopyTo(bReturn, Pos);
			Pos += 4;
			ConvertToLEBytes((Int32)(Rec.Long * 100000)).CopyTo(bReturn, Pos);
			Pos += 4;
			ConvertToLEBytes((Int32)(Rec.Lat * 100000)).CopyTo(bReturn, Pos);
			Pos += 4;
			Encoding.ASCII.GetBytes(Rec.Name).CopyTo(bReturn, Pos);
			Pos += Rec.Name.Length;
			
			if(!String.IsNullOrEmpty(Rec.Telephone)) {
				bReturn[Pos] = 0x3E;
				Pos += 1;
				Encoding.ASCII.GetBytes(Rec.Telephone).CopyTo(bReturn, Pos);
				Pos += Rec.Telephone.Length;
			}
			bReturn[Pos] = 0x00;
			return bReturn;
		}
		#endregion
		
		/// <summary>
		/// Converts Int32 to Little Endian byte array
		/// </summary>
		/// <param name="BEVal">The value to convert</param>
		/// <returns>The Byte array in Little Endian</returns>
		private byte[] ConvertToLEBytes(Int32 BEVal) {
			byte[] bReturn = null;
			bReturn = BitConverter.GetBytes(BEVal);
			return bReturn;
		}
		
		/// <summary>
		/// Converts a 4 byte signed int little endian, to Int32
		/// </summary>
		/// <param name="LittleEndianBytes">The 4 bytes in Little Endian</param>
		/// <returns>A signed Int32 representing the number</returns>
		private Int32 SignedLELatLongToInt32(byte[] LittleEndianBytes) {
			Int32 iReturn = 0;
			Int32 BigEndian = SwapEndian(BitConverter.ToInt32(LittleEndianBytes, 0));
			
			//If this this a negative number, then convert using 2s compliment
			if((LittleEndianBytes[3] & SignedBit) == SignedBit) {
				iReturn = (~BigEndian) + 1;
			}
			//Sign bit is 0, therefore its positive
			else {
				iReturn = BigEndian;
			}
			return iReturn;
		}
		
		/// <summary>
		/// Converts Little Endian to Big Endian or Big Endian to Little Endian
		/// </summary>
		/// <param name="Val">The value to convert</param>
		/// <returns>The value with endian swapped</returns>
		private Int32 SwapEndian(Int32 Val) {
			return (Int32)((Val << 24) & 0xFF000000) + ((Val << 8) & 0x00FF0000) + ((Val >> 8) & 0x0000FF00) + ((Val >> 24) & 0x000000FF);
		}
	}
}
