using System.Collections.Generic;

namespace SubD.Builders
{
    public interface IGeneratorIdentity
    {
        // just here to hint what the values of SetForbidSpecificMerge
    }

    public interface IHasGeneratiorIdentities
    {
        HashSet<IGeneratorIdentity> GIs { get; }
    }
}