using System.Collections.Generic;

namespace Holang.Core.Runtime;

public static class HolangRunner {
    public static Rollout Run(Holoware ware, IHolangSampler sampler, Dictionary<string, object?>? env = null) {
        var rollout = new Rollout();
        var phore = new Holophore(loom: new object(), rollout: rollout, env: env, sampler: sampler);
        ware.Invoke(phore);
        return rollout;
    }
}

