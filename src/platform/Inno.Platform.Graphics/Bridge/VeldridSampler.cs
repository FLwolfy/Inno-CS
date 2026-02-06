using System;
using Veldrid;
using InnoSamplerDesc = Inno.Platform.Graphics.SamplerDescription;
using InnoSamplerFilter = Inno.Platform.Graphics.SamplerFilter;
using InnoSamplerAddress = Inno.Platform.Graphics.SamplerAddressMode;
using VeldridSamplerDesc = Veldrid.SamplerDescription;
using VeldridSamplerFilter = Veldrid.SamplerFilter;
using VeldridSamplerAddress = Veldrid.SamplerAddressMode;

namespace Inno.Platform.Graphics.Bridge;

internal sealed class VeldridSampler : ISampler
{
    internal readonly Sampler inner;

    private VeldridSampler(Sampler sampler)
    {
        inner = sampler;
    }

    public static VeldridSampler Create(GraphicsDevice graphicsDevice, InnoSamplerDesc desc)
    {
        Sampler sampler = graphicsDevice.ResourceFactory.CreateSampler(
            ToVeldridSamplerDesc(desc)
        );

        return new VeldridSampler(sampler);
    }

    private static VeldridSamplerDesc ToVeldridSamplerDesc(InnoSamplerDesc desc)
    {
        return new VeldridSamplerDesc
        {
            Filter = ToVeldridFilter(desc.filter),
            AddressModeU = ToVeldridAddress(desc.addressU),
            AddressModeV = ToVeldridAddress(desc.addressV),
            AddressModeW = VeldridSamplerAddress.Clamp,

            MaximumAnisotropy = 1,
            MinimumLod = 0,
            MaximumLod = uint.MaxValue,
            LodBias = 0
        };
    }

    private static VeldridSamplerFilter ToVeldridFilter(InnoSamplerFilter filter)
    {
        return filter switch
        {
            InnoSamplerFilter.Point  => VeldridSamplerFilter.MinPoint_MagPoint_MipPoint,
            InnoSamplerFilter.Linear => VeldridSamplerFilter.MinLinear_MagLinear_MipLinear,
            _ => throw new NotSupportedException($"Unsupported sampler filter: {filter}")
        };
    }

    private static VeldridSamplerAddress ToVeldridAddress(InnoSamplerAddress address)
    {
        return address switch
        {
            InnoSamplerAddress.Clamp  => VeldridSamplerAddress.Clamp,
            InnoSamplerAddress.Repeat => VeldridSamplerAddress.Wrap,
            _ => throw new NotSupportedException($"Unsupported sampler address mode: {address}")
        };
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
