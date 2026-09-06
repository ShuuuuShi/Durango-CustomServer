using System.Collections.Generic;
using Newtonsoft.Json;

namespace Durango.Online;

/// <summary>สถานะที่ดินที่เซฟใน .world — แปลงเป็น EstateLicense ตอนส่งให้เกม</summary>
public class EstateRecord
{
	[JsonProperty("type")]
	public int Type;

	[JsonProperty("owner_id")]
	public string OwnerId;

	[JsonProperty("activated_at")]
	public double ActivatedAt;

	[JsonProperty("expires_at")]
	public double? ExpiresAt;

	[JsonProperty("size")]
	public int Size;

	[JsonProperty("region_id")]
	public string RegionId;

	[JsonProperty("tile_x")]
	public int TileX;

	[JsonProperty("tile_y")]
	public int TileY;

	[JsonProperty("cells")]
	public List<string> Cells = new();

	[JsonProperty("access_for_others")]
	public int? AccessForOthers;
}
