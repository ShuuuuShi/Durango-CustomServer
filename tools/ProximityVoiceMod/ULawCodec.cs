using System;

namespace ProximityVoiceMod
{
    /// <summary>
    /// G.711 µ-law — เบาพอสำหรับ MVP บน Mono net35 โดยไม่พึ่ง native Opus
    /// (คุณภาพพอสำหรับ proximity voice; อัปเกรด Opus ทีหลังได้โดยไม่เปลี่ยนโปรโตคอล flags)
    /// </summary>
    internal static class ULawCodec
    {
        private const int BIAS = 0x84;
        private const int CLIP = 32635;

        public static byte[] Encode(float[] samples, int count)
        {
            byte[] output = new byte[count];
            for (int i = 0; i < count; i++)
            {
                float f = samples[i];
                if (f > 1f) f = 1f;
                if (f < -1f) f = -1f;
                short pcm = (short)(f * 32767f);
                output[i] = LinearToULaw(pcm);
            }
            return output;
        }

        public static void Decode(byte[] input, float[] output)
        {
            int n = Math.Min(input.Length, output.Length);
            for (int i = 0; i < n; i++)
            {
                short pcm = ULawToLinear(input[i]);
                output[i] = pcm / 32768f;
            }
            for (int i = n; i < output.Length; i++) output[i] = 0f;
        }

        public static float Rms(float[] samples, int count)
        {
            if (count <= 0) return 0f;
            double sum = 0;
            for (int i = 0; i < count; i++)
            {
                double v = samples[i];
                sum += v * v;
            }
            return (float)Math.Sqrt(sum / count);
        }

        private static byte LinearToULaw(short pcm)
        {
            int sample = pcm;
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = -sample;
            if (sample > CLIP) sample = CLIP;
            sample += BIAS;
            int exponent = 7;
            for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }
            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            byte ulaw = (byte)(~(sign | (exponent << 4) | mantissa));
            return ulaw;
        }

        private static short ULawToLinear(byte ulaw)
        {
            ulaw = (byte)~ulaw;
            int sign = ulaw & 0x80;
            int exponent = (ulaw >> 4) & 0x07;
            int mantissa = ulaw & 0x0F;
            int sample = ((mantissa << 3) + BIAS) << exponent;
            sample -= BIAS;
            return (short)(sign != 0 ? -sample : sample);
        }
    }
}
