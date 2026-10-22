void YCbCrToRGB_half(half3 RGB, half Y, half2 CbCr, out half3 Out)
{
#if defined(UNITY_PLATFORM_ANDROID)
    Out = RGB;
#elif defined(UNITY_PLATFORM_IOS)
    half Cb = CbCr.x;
    half Cr = CbCr.y;

    Out.r = Y + 1.4020 * (Cr - 0.5);
    Out.g = Y - 0.3441 * (Cb - 0.5)
              - 0.7141 * (Cr - 0.5);
    Out.b = Y + 1.7720 * (Cb - 0.5);
#endif
}