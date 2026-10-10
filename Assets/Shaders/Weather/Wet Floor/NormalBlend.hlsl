void NormalBlend3_Reoriented_half(half3 A, half3 B, half3 C, out half3 Out)
{
    half3 t = A + half3(0.0, 0.0, 1.0);
    half3 u = B * half3(-1.0, -1.0, 1.0);

    half3 AB = (t / t.z) * dot(t, u) - u;
    
    t = AB + half3(0.0, 0.0, 1.0);
    u = C * half3(-1.0, -1.0, 1.0);

    Out = (t / t.z) * dot(t, u) - u;
}