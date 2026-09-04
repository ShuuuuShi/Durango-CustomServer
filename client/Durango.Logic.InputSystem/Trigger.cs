using System;

namespace Durango.Logic.InputSystem;

[Flags]
public enum Trigger
{
	None = 0,
	Down = 1,
	Up = 2,
	Press = 4,
	Stream = Down | Press,
	UpStream = Up | Press,
	DownUp = Down | Up,
	All = -1
}
