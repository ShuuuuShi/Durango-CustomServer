public struct MusicId
{
	public int? Slot;

	public string SharedId;

	public static implicit operator MusicId(int value)
	{
		return new MusicId
		{
			Slot = value
		};
	}

	public static implicit operator MusicId(string value)
	{
		return new MusicId
		{
			SharedId = value
		};
	}

	public bool IsEqual(MusicId target)
	{
		if (Slot.HasValue)
		{
			return target.Slot.HasValue && Slot.Value == target.Slot.Value;
		}
		if (!string.IsNullOrEmpty(SharedId))
		{
			return SharedId == target.SharedId;
		}
		return false;
	}
}
