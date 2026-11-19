// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
using System.Collections.Immutable;
using Morphir.Compilation.Mep;
using Morphir.IR.Trees;
using Morphir.Mep;

namespace Morphir.Tooling.Features.Generate;

internal static class BoundedGenerationSession
{
    /// <summary>Drives an initialized session while the caller owns the snapshot read lease, guest and stage.</summary>
    /// <remarks>The caller supplies a bounded termination routine that joins the guest and its connection.</remarks>
    public static async Task<Outcome<StagedGenerationOutput>> RunAsync(
        HostSession host, BoundedTreeSnapshot snapshot, ModelAccessStart start,
        BoundedArtifactStage stage, Func<Task> terminate, CancellationToken cancellationToken,
        CancellationToken sessionDeadline = default)
    {
        var awaitingAdvance = false;
        Task? pendingExchange = null;
        var limits = start.Limits;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionDeadline);
        if (limits.MaxSessionWorkUnits <= 0)
            return await AbortAsync(Operational("extension.bounded_work_limit", "The negotiated maxSessionWorkUnits must be positive."));
        var work = new BoundedTreeWorkBudget(limits.MaxSessionWorkUnits);
        var (bound, bindFailure) = MepGenerationSnapshot.BindForHost(snapshot, start, work).Match(
            static value => ((MepGenerationSnapshot?)value, (GenerationFailure?)null),
            static errors => (null, StoreFailure(errors)));
        if (bound is null) return await AbortAsync(bindFailure!);
        if (limits.MaxSessionMilliseconds <= 0 || limits.MaxCallMilliseconds <= 0)
            return await AbortAsync(Operational("extension.bounded_limits", "The generation deadlines must be positive."));

        if (!sessionDeadline.CanBeCanceled) deadline.CancelAfter(limits.MaxSessionMilliseconds);
        ModelAccessAdvance? advance = null;

        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!work.TryCharge(1))
                    return await AbortAsync(Operational("extension.bounded_work_limit", "Processing exceeds the negotiated maxSessionWorkUnits."));
                using var exchange = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                exchange.CancelAfter(limits.MaxCallMilliseconds);
                var reply = await AwaitExchangeAsync(advance is null
                    ? host.StartGenerationAsync(start, exchange.Token)
                    : host.AdvanceGenerationAsync(advance, exchange.Token), exchange.Token);

                var (step, replyError) = reply.Match(
                    static value => ((ModelAccessStep?)value, (string?)null),
                    static error => (null, error.Message));
                (int Round, IReadOnlyList<ModelAccessRequest> Requests)? continuation = null;
                if (step is not null)
                {
                    continuation = step.Match<(int, IReadOnlyList<ModelAccessRequest>)?>(
                        onContinue: static (_, round, requests, _, _) => (round, requests),
                        onComplete: static (_, _, _) => null,
                        onFailed: static (_, _) => null);
                    awaitingAdvance = continuation.HasValue;
                }
                if (exchange.IsCancellationRequested) return await AbortAsync(Interrupted());
                if (step is null)
                    return await AbortAsync(Operational("extension.bounded_exchange", replyError!));

                var staged = stage.Apply(step, deadline.Token, work);
                if (staged is Outcome<StagedGenerationOutput?>.Failure refusal)
                {
                    var failure = refusal.Error;
                    if (failure.Kind == GenerationFailureKind.Cancelled && deadline.IsCancellationRequested
                        && !cancellationToken.IsCancellationRequested)
                    {
                        var timeout = Interrupted();
                        failure = timeout with { Diagnostics = timeout.Diagnostics.AddRange(failure.Diagnostics.Skip(1)) };
                    }
                    return await AbortAsync(failure, cleanStage: false);
                }
                if (staged is Outcome<StagedGenerationOutput?>.Success { Value: { } output })
                    return new Outcome<StagedGenerationOutput>.Success(output);
                if (continuation is not { } next)
                    return await AbortAsync(Operational("extension.bounded_exchange", "The guest did not complete generation."));

                var (delivery, deliveryFailure) = bound.PrepareHostAdvance(next.Round, next.Requests).Match(
                    static value => ((ModelAccessAdvance?)value, (GenerationFailure?)null),
                    static errors => (null, StoreFailure(errors)));
                deadline.Token.ThrowIfCancellationRequested();
                if (delivery is null) return await AbortAsync(deliveryFailure!);
                advance = delivery;
            }
        }
        catch (OperationCanceledException)
        {
            return await AbortAsync(Interrupted());
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            return await AbortAsync(Operational("extension.bounded_exchange", error.Message));
        }

        async Task<T> AwaitExchangeAsync<T>(Task<T> request, CancellationToken token)
        {
            pendingExchange = request;
            // Framed cancellation sends a notification. A peer may ignore it and never reply.
            var reply = await request.WaitAsync(token);
            pendingExchange = null;
            return reply;
        }

        GenerationFailure Interrupted() => cancellationToken.IsCancellationRequested
            ? GenerationFailure.One(GenerationFailureKind.Cancelled, "cancelled", "Generation was cancelled.")
            : Operational("extension.bounded_deadline", "The generation deadline expired.");

        async Task<Outcome<StagedGenerationOutput>> AbortAsync(GenerationFailure failure, bool cleanStage = true)
        {
            if (pendingExchange is not null || (awaitingAdvance && deadline.IsCancellationRequested))
                failure = await TerminateAsync(failure);
            else if (awaitingAdvance)
            {
                using var cancelBudget = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                cancelBudget.CancelAfter(limits.MaxCallMilliseconds);
                try
                {
                    var cancelled = await AwaitExchangeAsync(
                        host.CancelGenerationAsync(new ModelAccessCancel(start.SessionId), cancelBudget.Token), cancelBudget.Token);
                    var cancelError = cancelled.Match(static _ => (string?)null, static error => error.Message);
                    if (cancelError is not null)
                        failure = await TerminateAsync(WithCancelFailure(failure, cancelError));
                }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
                {
                    var message = error is OperationCanceledException
                        ? "The cancellation acknowledgement did not arrive by the deadline."
                        : error.Message;
                    failure = await TerminateAsync(WithCancelFailure(failure, message));
                }
            }

            if (cleanStage && stage.Cleanup() is { } cleanupError)
                failure = failure with { Diagnostics = failure.Diagnostics.Add(cleanupError) };
            return new Outcome<StagedGenerationOutput>.Failure(failure);
        }

        async Task<GenerationFailure> TerminateAsync(GenerationFailure failure)
        {
            try
            {
                await terminate();
            }
            catch (Exception error)
            {
                failure = WithTerminationFailure(failure, error.Message);
            }

            if (pendingExchange is { } pending)
            {
                try { await pending.WaitAsync(TimeSpan.FromMilliseconds(limits.MaxCallMilliseconds)); }
                catch (TimeoutException)
                {
                    failure = WithTerminationFailure(failure, "The guest owner did not settle the pending exchange.");
                    _ = pending.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch (Exception)
                {
                    // Observe the pending request's fault after termination. Keep the original operation error.
                }
                pendingExchange = null;
            }
            return failure;
        }
    }

    private static GenerationFailure StoreFailure(IEnumerable<StoreError> errors) =>
        new(GenerationFailureKind.Operational,
            errors.Select(error => new GenerationDiagnostic(error.Code, error.Message, error.FileOrNull)).ToImmutableArray());

    private static GenerationFailure Operational(string code, string message) =>
        GenerationFailure.One(GenerationFailureKind.Operational, code, message);

    private static GenerationFailure WithCancelFailure(GenerationFailure failure, string message) =>
        failure with { Diagnostics = failure.Diagnostics.Add(new("extension.bounded_cancel", message)) };

    private static GenerationFailure WithTerminationFailure(GenerationFailure failure, string message) =>
        failure with { Diagnostics = failure.Diagnostics.Add(new("extension.bounded_termination", message)) };
}
