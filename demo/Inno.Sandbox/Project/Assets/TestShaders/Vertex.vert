#version 450

layout(location = 0) in vec2 position;
layout(location = 1) in vec4 color;

layout(set = 0, binding = 0) uniform Transform
{
   mat4 uPosition;
   mat4 uRotation;
   mat4 uScale;
};

layout(location = 0) out vec4 fsin_Color;

void main()
{
   mat4 transform = uPosition * uRotation * uScale;
   gl_Position = transform * vec4(position, 0, 1);
   fsin_Color = color;
}