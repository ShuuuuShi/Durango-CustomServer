using Messages;
using Shared.System;

namespace Durango.Logic.Timeline;

public struct TimelineLog
{
	public struct ArtifactDigest
	{
		public string PrototypeId;

		public string EntityId;

		public string RegionId;

		public int[] Tile;

		public Messages.ArtifactDigest? ToArtifactDigest()
		{
			Point2 tile = ((Tile != null && Tile.Length < 2) ? new Point2(Tile[0], Tile[2]) : Point2.zero);
			return new Messages.ArtifactDigest
			{
				EntityId = EntityId,
				PrototypeId = PrototypeId,
				RegionId = RegionId,
				Tile = tile
			};
		}
	}

	public TimelineEvent Type;

	public double At;

	public ArtifactDigest? TargetArtifact;

	public string TargetEntityId;

	public string AgentEntityId;

	public Gettext[] Params;
}
