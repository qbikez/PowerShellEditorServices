//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.PowerShell.EditorServices.Protocol.Client;
using Microsoft.PowerShell.EditorServices.Protocol.DebugAdapter;
using Microsoft.PowerShell.EditorServices.Protocol.MessageProtocol.Channel;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.PowerShell.EditorServices.Test.Host
{
    public class DebugAdapterTests : ServerTestsBase, IAsyncLifetime
    {
        private DebugAdapterClient debugAdapterClient;
        private string DebugScriptPath = 
            Path.GetFullPath(@"..\..\..\PowerShellEditorServices.Test.Shared\Debugging\DebugTest.ps1");

        public Task InitializeAsync()
        {
            string testLogPath =
                Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "logs",
                    this.GetType().Name,
                    Guid.NewGuid().ToString().Substring(0, 8) + ".log");

            System.Console.WriteLine("        Output log at path: {0}", testLogPath);

            this.protocolClient =
            this.debugAdapterClient =
                new DebugAdapterClient(
                    new StdioClientChannel(
                        "Microsoft.PowerShell.EditorServices.Host.exe",
                        "/debugAdapter",
                        "/logPath:\"" + testLogPath + "\"",
                        "/logLevel:Verbose"));

            return this.debugAdapterClient.Start();
        }

        public Task DisposeAsync()
        {
            return this.debugAdapterClient.Stop();
        }

        [Fact]
        public async Task DebugAdapterStopsOnBreakpoints()
        {
            await this.SendRequest(
                SetBreakpointsRequest.Type,
                new SetBreakpointsRequestArguments
                {
                    Source = new Source
                    {
                        Path = DebugScriptPath
                    },
                    Lines = new int[] { 5, 7 }
                });

            Task<StoppedEventBody> breakEventTask = this.WaitForEvent(StoppedEvent.Type);
            await this.LaunchScript(DebugScriptPath);

            // Wait for a couple breakpoints
            StoppedEventBody stoppedDetails = await breakEventTask;
            Assert.Equal(DebugScriptPath, stoppedDetails.Source.Path);
            Assert.Equal(5, stoppedDetails.Line);

            breakEventTask = this.WaitForEvent(StoppedEvent.Type);
            await this.SendRequest(ContinueRequest.Type, new object());
            stoppedDetails = await breakEventTask;
            Assert.Equal(DebugScriptPath, stoppedDetails.Source.Path);
            Assert.Equal(7, stoppedDetails.Line);

            // Abort script execution
            Task terminatedEvent = this.WaitForEvent(TerminatedEvent.Type);
            await 
                Task.WhenAll(
                    this.SendRequest(DisconnectRequest.Type, new object()),
                    terminatedEvent);
        }

        [Fact]
        public async Task DebugAdapterReceivesOutputEvents()
        {
            OutputReader outputReader = new OutputReader(this.debugAdapterClient);

            await this.LaunchScript(DebugScriptPath);

            // Make sure we're getting output from the script
            Assert.Equal("Output 1", await outputReader.ReadLine());

            // Abort script execution
            Task terminatedEvent = this.WaitForEvent(TerminatedEvent.Type);
            await
                Task.WhenAll(
                    this.SendRequest(DisconnectRequest.Type, new object()),
                    terminatedEvent);
        }

        private Task LaunchScript(string scriptPath)
        {
            return this.debugAdapterClient.LaunchScript(scriptPath);
        }
    }
}

