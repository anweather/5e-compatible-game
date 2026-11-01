// Turns a colour round the colour wheel, about grey, so grey stays grey and an orange fire's white
// heart stays white (LookTint, whose Of does the same to a colour in code). `turn` is in radians,
// red toward green; 0 leaves the colour exactly as it was. It works on display colours, as a colour's
// hue is read, and comes back to the linear colours the frame is drawn in.
#ifndef HUE_TURN_INCLUDED
#define HUE_TURN_INCLUDED

float3 HueTurn(float3 color, float turn)
{
    if (turn == 0.0) return color;
    static const float3 Grey = float3(0.57735, 0.57735, 0.57735);
    float3 c = pow(max(color, 0.0), 1.0 / 2.2);
    float s = sin(turn), k = cos(turn);
    c = c * k + cross(Grey, c) * s + Grey * dot(Grey, c) * (1.0 - k);
    return pow(max(c, 0.0), 2.2);
}

#endif
