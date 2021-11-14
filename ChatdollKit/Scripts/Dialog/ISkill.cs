﻿using System.Threading;
using System.Threading.Tasks;

namespace ChatdollKit.Dialog
{
    public interface ISkill
    {
        string TopicName { get; }
        bool IsAvailable { get; }
        Task<Response> PreProcessAsync(Request request, State state, CancellationToken token);
        Task ShowWaitingAnimationAsync(Response response, Request request, State state, CancellationToken token);
        Task<Response> ProcessAsync(Request request, State state, CancellationToken token);
        Task ShowResponseAsync(Response response, Request request, State state, CancellationToken token);
    }
}