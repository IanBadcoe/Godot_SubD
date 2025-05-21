using Geom_Util;

namespace SubD.Interfaces;

public interface ISubdivider
{
    Surface Subdivide(Surface input);
}