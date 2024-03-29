﻿using Moq;
using System.Threading;
using Up2dateDotNet;

namespace Tests_Shared
{
    public class WrapperMock : Mock<IWrapper>
    {
        private ManualResetEvent runExitEvent = new ManualResetEvent(false);

        public ProvErrorCallbackFunc ProvErrorCallback { get; private set; }
        public ProvSuccessCallbackFunc ProvSuccessCallback { get; private set; }
        public ConfigRequestFunc ConfigRequestFunc { get; private set; }
        public DeploymentActionFunc DeploymentActionFunc { get; private set; }
        public CancelActionFunc CancelActionFunc { get; private set; }
        public NoActionFunc NoActionFunc { get; private set; }

        public WrapperMock()
        {
            Setup(m => m.RunClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsNotNull<ProvErrorCallbackFunc>(), It.IsNotNull<ProvSuccessCallbackFunc>(),
                It.IsAny<ConfigRequestFunc>(), It.IsAny<DeploymentActionFunc>(), It.IsAny<CancelActionFunc>(), It.IsAny<NoActionFunc>()))
                .Callback<string, string, string, ProvErrorCallbackFunc, ProvSuccessCallbackFunc, ConfigRequestFunc, DeploymentActionFunc, CancelActionFunc, NoActionFunc>((c, e, t, pe, ps, cr, da, ca, na) =>
                {
                    ProvErrorCallback = pe;
                    ProvSuccessCallback = ps;
                    ConfigRequestFunc = cr;
                    DeploymentActionFunc = da;
                    CancelActionFunc = ca;
                    NoActionFunc = na;
                    runExitEvent.WaitOne();
                });
        }

        public void ExitRun()
        {
            runExitEvent.Set();
        }
    }
}
