﻿/* Copyright 2019-present MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver.Core.Bindings;
using MongoDB.Driver.Core.Misc;

namespace MongoDB.Driver.Core.Operations
{
    internal sealed class RetryableReadContext : IDisposable
    {
        #region static

        public static RetryableReadContext Create(IReadBinding binding, bool retryRequested, CancellationToken cancellationToken)
        {
            var context = new RetryableReadContext(binding, retryRequested);
            try
            {
                context.Initialize(cancellationToken);

                ChannelPinningHelper.PinChannellIfRequired(
                    context.ChannelSource,
                    context.Channel,
                    context.Binding.Session);

                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        public static async Task<RetryableReadContext> CreateAsync(IReadBinding binding, bool retryRequested, CancellationToken cancellationToken)
        {
            var context = new RetryableReadContext(binding, retryRequested);
            try
            {
                await context.InitializeAsync(cancellationToken).ConfigureAwait(false);

                ChannelPinningHelper.PinChannellIfRequired(
                    context.ChannelSource,
                    context.Channel,
                    context.Binding.Session);

                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }
        #endregion

#pragma warning disable CA2213 // Disposable fields should be disposed
        private readonly IReadBinding _binding;
#pragma warning restore CA2213 // Disposable fields should be disposed
        private IChannelHandle _channel;
        private IChannelSourceHandle _channelSource;
        private bool _disposed;
        private bool _retryRequested;

        public RetryableReadContext(IReadBinding binding, bool retryRequested)
        {
            _binding = Ensure.IsNotNull(binding, nameof(binding));
            _retryRequested = retryRequested;
        }

        public IReadBinding Binding => _binding;
        public IChannelHandle Channel => _channel;
        public IChannelSourceHandle ChannelSource => _channelSource;
        public bool RetryRequested => _retryRequested;

        public void Dispose()
        {
            if (!_disposed)
            {
                _channelSource?.Dispose();
                _channel?.Dispose();
                _disposed = true;
            }
        }

        public void ReplaceChannel(IChannelHandle channel)
        {
            Ensure.IsNotNull(channel, nameof(channel));
            _channel?.Dispose();
            _channel = channel;
        }

        public void ReplaceChannelSource(IChannelSourceHandle channelSource)
        {
            Ensure.IsNotNull(channelSource, nameof(channelSource));
            _channelSource?.Dispose();
            _channel?.Dispose();
            _channelSource = channelSource;
            _channel = null;
        }

        private void Initialize(CancellationToken cancellationToken)
        {
            _channelSource = _binding.GetReadChannelSource(cancellationToken);

            try
            {
                _channel = _channelSource.GetChannel(cancellationToken);
            }
            catch (Exception ex) when (RetryableReadOperationExecutor.ShouldConnectionAcquireBeRetried(this, ex))
            {
                ReplaceChannelSource(_binding.GetReadChannelSource(cancellationToken));
                ReplaceChannel(_channelSource.GetChannel(cancellationToken));
            }
        }

        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            _channelSource = await _binding.GetReadChannelSourceAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                _channel = await _channelSource.GetChannelAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (RetryableReadOperationExecutor.ShouldConnectionAcquireBeRetried(this, ex))
            {
                ReplaceChannelSource(await _binding.GetReadChannelSourceAsync(cancellationToken).ConfigureAwait(false));
                ReplaceChannel(await _channelSource.GetChannelAsync(cancellationToken).ConfigureAwait(false));
            }
        }
    }
}
