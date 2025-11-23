struct vin
{
    half4 position : POSITION;
    fixed4 color : COLOR;
    half2 uv : TEXCOORD0;
};

struct v2f
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 color: COLOR;
};

struct ulong
{
    uint lo;
    uint hi;
};

uniform float4 _Settings;
uniform float4x4 _ViewProjectionMatrix;
uniform StructuredBuffer<ulong> _Buffer;

sampler2D _MainTex;
float4 _MainTex_ST;

static const float2 QUAD_VERTICES[6] = {
    float2(0, 1),
    float2(1, 1),
    float2(1, 0),
    float2(1, 0),
    float2(0, 0),
    float2(0, 1),
};

static const float4 COLOR_TABLE[8] = {
    float4(0.92f, 0.94f, 0.94f, 1), // white rgb(236, 240, 241)
    float4(0.75f, 0.22f, 0.17f, 1), // red rgb(192, 57, 43)
    float4(0.10f, 0.77f, 0.61f, 1), // green rgb(26, 188, 156)
    float4(0.94f, 0.77f, 0.05f, 1), // yellow rgb(241, 196, 15)
    float4(0.20f, 0.59f, 0.86f, 1), // blue rgb(52, 152, 219)
    float4(0.30f, 0.82f, 0.88f, 1), // cyan rgb(77, 208, 225)
    float4(0.67f, 0.27f, 0.74f, 1), // magenta rgb(171, 71, 188)
    float4(0.13f, 0.13f, 0.13f, 0.4f), // black
};

v2f QuadDrawVertex(const uint id : SV_VertexID, uint instanceID: SV_InstanceID)
{
    const uint quadCornerId = id % 6;

    const ulong value = _Buffer[instanceID];
    const float posX = float(value.lo & 0xFFF);
    const float posY = float((value.lo >> 12) & 0x7FF);
    const float sizeX = float((value.lo >> 23) & 0x7FF | (value.hi & 0x3) << 9);
    const float sizeY = float((value.hi >> 2) & 0x7FF);
    const float texU = float((value.hi >> 13) & 0xFF) / 0xFF;
    const float texV = float((value.hi >> 21) & 0xFF) / 0xFF;
    const uint colorIndex = (value.hi >> 29) & 0x7;
    const float2 quadCorner = QUAD_VERTICES[quadCornerId];
    const float2 posQuadCorner = quadCorner * float2(sizeX, sizeY);
    const float2 texUV = float2(texU, texV) + quadCorner * float2(sizeX, sizeY) / _Settings.zw;

    v2f o;
    o.position = mul(_ViewProjectionMatrix, float4(posX + posQuadCorner.x, posY + posQuadCorner.y, 0.0f, 1.0f));
    o.uv = float2(texUV.x, 1.0 - texUV.y);
    o.color = COLOR_TABLE[colorIndex];
    return o;
}

v2f QuadDrawSimpleVertex(vin v)
{
    v2f o;
    o.position = mul(_ViewProjectionMatrix, v.position);
    o.uv = v.uv;
    o.color = v.color;
    return o;
}

fixed4 QuadDrawFragment(const v2f i) : SV_Target
{
    const float4 col = tex2D(_MainTex, i.uv);
    return fixed4(i.color.rgb, i.color.a * col.r);
}
