namespace Durango.System;

/// <summary>
/// [เพิ่มเอง 6 ก.ย. 2026] ที่อยู่เซิร์ฟที่ถูก "ฝัง" มากับตัวเกมตอน build
///
/// ═══ ทำไมต้องมี (เฉพาะ Android) ═══
/// บน PC เปลี่ยนเซิร์ฟได้ด้วยการวาง <c>clusters.json</c> ข้าง <c>Durango.exe</c>
/// (<c>TitleMenuGroup.ReadLocalClusterJson</c>) แต่บน Android ไฟล์นั้นต้องไปอยู่ที่
/// <c>Application.persistentDataPath</c> ซึ่งอยู่ในพื้นที่ส่วนตัวของแอป —
/// **ผู้เล่นวางไฟล์ลงไปเองไม่ได้ก่อนเปิดเกมครั้งแรก** (ต้อง root หรือใช้ adb)
///
/// ⇒ ตอน build APK ให้เครื่องมือเขียนที่อยู่ลงตรงนี้แทน (tools/android/build-android.ps1)
/// ลำดับที่ตัวเกมใช้จึงเป็น:
///   1. <c>clusters.json</c> ที่วางไว้เอง  (PC · หรือ Android ที่ push ไฟล์เข้าไปได้)
///   2. <c>Json</c> ที่ฝังมาตอน build       (Android ปกติ)
///   3. TextAsset <c>offline/clusters</c> ในเกม (ของ NEXON เดิม)
///
/// ค่าว่าง = ไม่ได้ฝังอะไรมา ⇒ ข้ามไปขั้นถัดไป ⇒ **การ build ฝั่ง PC ไม่เปลี่ยนพฤติกรรมเลย**
/// </summary>
public static class BakedCluster
{
	/// <summary>
	/// เนื้อ <c>clusters.json</c> ที่ฝังมา — เครื่องมือ build เขียนทับบรรทัดนี้
	///
	/// ⚠️ ห้ามแก้ด้วยมือแล้ว commit ค่าจริงลง repo — ที่อยู่เซิร์ฟเป็นของ deploy ไม่ใช่ของซอร์ส
	/// (build-android.ps1 เขียนทับตอน build แล้วคืนค่าเดิมให้เสมอ แม้ build ล้มกลางคัน)
	/// </summary>
	public const string Json = "";
}
