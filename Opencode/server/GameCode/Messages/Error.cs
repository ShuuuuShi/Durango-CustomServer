using MsgPack;

namespace Messages;

public struct Error
{
	public const uint TypeCode = 1022u;

	public string TypeName;

	public string Text;

	public static void Pack(Packer packer, Error val, bool hint = false)
	{
		if (hint)
		{
			packer.PackArrayHeader(3);
			packer.Pack(1022u);
		}
		else
		{
			packer.PackArrayHeader(2);
		}
if (val.TypeName == null)
			{
				packer.PackString(string.Empty);
			}
			else
			{
				packer.PackString(val.TypeName);
			}
			// Text เป็น null ได้เมื่อส่ง default(Error) — กัน PackString(null) โยน NRE
			packer.PackString(val.Text ?? string.Empty);
	}

	public static Error Unpack(Unpacker unpacker)
	{
		unpacker.Read();
		Error result = default(Error);
		result.TypeName = unpacker.LastReadData.AsString();
		unpacker.Read();
		result.Text = LocalizeSystem.UnpackGettextFromMsgPack(unpacker);
		return result;
	}

	public override string ToString()
	{
		return $"<Error TypeName={TypeName} Text={Text}>";
	}
}
