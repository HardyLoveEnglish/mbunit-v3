// Copyright 2005-2009 Gallio Project - http://www.gallio.org/
// Portions Copyright 2000-2004 Jonathan de Halleux
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Gallio.Common;
using Gallio.Common.Collections;
using Gallio.Common.Policies;
using Gallio.Common.Concurrency;
using Gallio.Model;
using Gallio.Common.Diagnostics;
using Gallio.Common.Markup;

namespace Gallio.Framework
{
    /// <summary>
    /// <para>
    /// A sandbox is an isolated environments for executing test actions.  It provides
    /// the ability to abort actions in progress so that the test runner can proceed
    /// to run other actions.
    /// </para>
    /// <para>
    /// Sandboxes are hierarchically structured.  When an outer sandbox is aborted, all
    /// of its inner sandboxes are likewise aborted.  A sandbox also provides the ability
    /// to create new child sandboxes at will so that test actions can be isolated with
    /// fine granularity.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This class is safe for use from multiple concurrent threads.
    /// </remarks>
    [SystemInternal]
    public sealed class Sandbox : IDisposable
    {
        private Sandbox parent;

        private readonly object syncRoot = new object();
        private List<Pair<ThreadAbortScope, Thread>> scopesAndThreads;
        private TestOutcome? abortOutcome;
        private string abortMessage;
        private event EventHandler aborted;
        private bool alreadyLoggedAbortOnce;
        private bool isDisposed;

        /// <summary>
        /// Creates a root sandbox.
        /// </summary>
        public Sandbox()
        {
        }

        /// <summary>
        /// <para>
        /// An event that is dispatched when <see cref="Abort(TestOutcome, string)" /> is called.
        /// </para>
        /// <para>
        /// If the sandbox has already been aborted then the event handler is immediately invoked.
        /// </para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public event EventHandler Aborted
        {
            add
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();

                    if (!abortOutcome.HasValue)
                    {
                        aborted += value;
                        return;
                    }
                }

                EventHandlerPolicy.SafeInvoke(value, this, EventArgs.Empty);
            }
            remove
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();

                    aborted -= value;
                }
            }
        }

        /// <summary>
        /// Returns true if <see cref="Abort(TestOutcome, string)" /> was called.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public bool WasAborted
        {
            get
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    return abortOutcome.HasValue;
                }
            }
        }

        /// <summary>
        /// Returns the <see cref="TestOutcome" /> passed to <see cref="Abort(TestOutcome, string)" />,
        /// or null if <see cref="Abort(TestOutcome, string)" /> has not been called.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public TestOutcome? AbortOutcome
        {
            get
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    return abortOutcome;
                }
            }
        }

        /// <summary>
        /// Gets a message that will be logged when the sandbox is aborted, or null if none.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public string AbortMessage
        {
            get
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();
                    return abortMessage;
                }
            }
        }

        /// <summary>
        /// <para>
        /// Disposes the sandbox.
        /// </para>
        /// <para>
        /// All currently executing actions are aborted with <see cref="TestOutcome.Error" />
        /// if <see cref="Abort(TestOutcome, string)" /> has not already been called.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            Abort(TestOutcome.Error, "The sandbox was disposed.", false);

            Sandbox cachedParent;
            lock (syncRoot)
            {
                if (isDisposed)
                    return;

                cachedParent = parent;
                parent = null;
                isDisposed = true;
            }

            if (cachedParent != null)
                cachedParent.Aborted -= HandleParentAborted;
        }

        /// <summary>
        /// <para>
        /// Creates a child sandbox.
        /// </para>
        /// <para>
        /// When the parent sandbox is aborted, the child will likewise be aborted.  This policy
        /// offers a mechanism to scope actions recursively.
        /// </para>
        /// </summary>
        /// <returns>The child sandbox</returns>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public Sandbox CreateChild()
        {
            ThrowIfDisposed();

            Sandbox child = new Sandbox();
            child.parent = this;
            Aborted += child.HandleParentAborted;
            return child;
        }

        /// <summary>
        /// <para>
        /// Aborts all actions in progress within this context.
        /// </para>
        /// <para>
        /// The abort is persistent and cannot be reverted.  Therefore once aborted, no further
        /// test actions will be permitted to run.  Subsequent calls to <see cref="Abort(TestOutcome, string)" />
        /// will have no effect.
        /// </para>
        /// </summary>
        /// <param name="outcome">The outcome to be returned from aborted actions</param>
        /// <param name="message">A message to be logged when the action is aborted, or null if none</param>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public void Abort(TestOutcome outcome, string message)
        {
            Abort(outcome, message, true);
        }

        private void Abort(TestOutcome outcome, string message, bool throwIfDisposed)
        {
            EventHandler cachedHandler;
            ThreadAbortScope[] cachedScopes;
            lock (syncRoot)
            {
                if (throwIfDisposed)
                    ThrowIfDisposed();

                if (abortOutcome.HasValue)
                    return;

                abortOutcome = outcome;
                abortMessage = message;

                cachedScopes = scopesAndThreads != null
                    ? GenericCollectionUtils.ConvertAllToArray(scopesAndThreads, pair => pair.First)
                    : null;
                scopesAndThreads = null;

                cachedHandler = aborted;
                aborted = null;
            }

            if (cachedScopes != null)
            {
                foreach (ThreadAbortScope scope in cachedScopes)
                    scope.Abort();
            }

            EventHandlerPolicy.SafeInvoke(cachedHandler, this, EventArgs.Empty);
        }

        /// <summary>
        /// Uses a specified timeout for all actions run within a block of code.
        /// </summary>
        /// <param name="timeout">The execution timeout or null if none</param>
        /// <param name="action">The action to perform, protected by the timeout</param>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="timeout"/> is negative</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="action"/> is null</exception>
        public void UseTimeout(TimeSpan? timeout, Action action)
        {
            if (timeout.HasValue && timeout.Value.Ticks < 0)
                throw new ArgumentOutOfRangeException("timeout", "Timeout must not be negative.");
            if (action == null)
                throw new ArgumentNullException("action");

            ThrowIfDisposed();

            using (timeout.HasValue ? new Timer(delegate
                {
                    Abort(TestOutcome.Timeout,
                        String.Format("The test timed out after {0} seconds.", timeout.Value.TotalSeconds),
                        false);
                }, null, (int) timeout.Value.TotalMilliseconds, Timeout.Infinite) : null)
            {
                action();
            }
        }

        /// <summary>
        /// Runs a test action.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the action throws an exception or if the test action is aborted, then logs a
        /// message to the <paramref name="markupDocumentWriter"/>.  Exceptions of type <see cref="TestException" />
        /// are handled specially since they may modify the effective outcome of the run.
        /// If <see cref="TestException.ExcludeStackTrace" /> is <c>true</c> and <see cref="TestException.HasNonDefaultMessage" />
        /// is <c>false</c> then the exception is effectively silent.  Therefore the action can modify
        /// its outcome and cause messages to be logged by throwing a suitable exception such as
        /// <see cref="TestException"/> or <see cref="SilentTestException" />.
        /// </para>
        /// <para>
        /// If the <see cref="Abort(TestOutcome, string)" /> method is called or has already been called, the action
        /// is aborted and the appropriate outcome is returned.  The abort is manifested as an
        /// asynchronous <see cref="ThreadAbortException" /> which should cause the action to
        /// terminate.  It may not terminate immediately, however.
        /// </para>
        /// <para>
        /// Produces an outcome in the following manner:
        /// <list type="bullet">
        /// <item>If the action completed without throwing an exception returns <see cref="TestOutcome.Passed"/>.</item>
        /// <item>If the action threw a <see cref="TestException" />, returns the value of the
        /// <see cref="TestException.Outcome" /> property.</item>
        /// <item>If the action threw an different kind of exception, logs
        /// the exception and returns <see cref="TestOutcome.Failed"/>.</item>
        /// <item>If the action was aborted, returns <see cref="AbortOutcome" />.</item>
        /// <item>If the action timed out, returns <see cref="TestOutcome.Timeout" />.</item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="markupDocumentWriter">The log writer for reporting failures</param>
        /// <param name="action">The action to run</param>
        /// <param name="description">A description of the action being performed,
        /// to be used as a log section name when reporting failures, or null if none</param>
        /// <returns>The outcome of the action</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="markupDocumentWriter"/> or <paramref name="action"/> is null</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the sandbox was disposed</exception>
        public TestOutcome Run(MarkupDocumentWriter markupDocumentWriter, Action action, string description)
        {
            if (markupDocumentWriter == null)
                throw new ArgumentNullException("testLogWriter");
            if (action == null)
                throw new ArgumentNullException("action");

            ThreadAbortScope scope = null;
            try
            {
                lock (syncRoot)
                {
                    ThrowIfDisposed();

                    if (!abortOutcome.HasValue)
                    {
                        if (scopesAndThreads == null)
                            scopesAndThreads = new List<Pair<ThreadAbortScope, Thread>>();

                        scope = new ThreadAbortScope();
                        scopesAndThreads.Add(new Pair<ThreadAbortScope, Thread>(scope, Thread.CurrentThread));
                    }
                }

                if (scope == null)
                    return HandleAbort(markupDocumentWriter, description, null);

                return RunWithScope(markupDocumentWriter, scope, action, description);
            }
            finally
            {
                if (scope != null)
                {
                    lock (syncRoot)
                    {
                        if (scopesAndThreads != null)
                        {
                            for (int i = 0; i < scopesAndThreads.Count; i++)
                                if (scopesAndThreads[i].First == scope)
                                    scopesAndThreads.RemoveAt(i);
                        }
                    }
                }
            }
        }

        private TestOutcome RunWithScope(MarkupDocumentWriter markupDocumentWriter, ThreadAbortScope scope, Action action, string description)
        {
            try
            {
                ThreadAbortException ex = scope.Run(action);
                if (ex != null)
                    return HandleAbort(markupDocumentWriter, description, ex);

                return TestOutcome.Passed;
            }
            catch (Exception ex)
            {
                // If the test itself threw a thread abort, not because we aborted it
                // ourselves but most likely due to a bug in the test subject, then we
                // prevent the abort from bubbling up any further.
                if (ex is ThreadAbortException &&
                    ! AppDomain.CurrentDomain.IsFinalizingForUnload())
                    Thread.ResetAbort();

                TestOutcome outcome;
                TestException testException = ex as TestException;
                if (testException != null)
                {
                    outcome = testException.Outcome;

                    if (testException.ExcludeStackTrace)
                        LogMessage(markupDocumentWriter, description, outcome, testException.HasNonDefaultMessage ? testException.Message : null, null);
                    else
                        LogMessage(markupDocumentWriter, description, outcome, null, testException);
                }
                else
                {
                    outcome = TestOutcome.Failed;
                    LogMessage(markupDocumentWriter, description, outcome, null, ex);
                }

                return outcome;
            }
        }

        /// <summary>
        /// Runs an action inside of a protected context wherein it cannot receive
        /// a thread abort from this <see cref="Sandbox"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method enables critical system code to be protected from aborts that
        /// may affect the sandbox.  This call cannot be nested.
        /// </para>
        /// </remarks>
        /// <param name="action">The action to run</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="action"/> is null</exception>
        [DebuggerHidden]
        public void Protect(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");

            ThreadAbortScope scope = FindActiveScopeForThread(Thread.CurrentThread);
            if (scope != null)
                scope.Protect(action);
            else
                action();
        }

        private ThreadAbortScope FindActiveScopeForThread(Thread currentThread)
        {
            lock (syncRoot)
            {
                // Choose the most recently added scope for the thread because if
                // the sandbox is called re-entrantly then we can reasonably assume
                // that Protect occurred in the outer scope.  Otherwise we'd be running
                // code somewhat unsafely...
                for (int i = scopesAndThreads.Count - 1; i >= 0; i--)
                {
                    if (scopesAndThreads[i].Second == currentThread)
                        return scopesAndThreads[i].First;
                }
            }

            return null;
        }

        private void HandleParentAborted(object sender, EventArgs e)
        {
            Sandbox parent = (Sandbox) sender;
            Abort(parent.AbortOutcome.Value, parent.AbortMessage);
        }

        private TestOutcome HandleAbort(MarkupDocumentWriter markupDocumentWriter, string actionDescription, ThreadAbortException ex)
        {
            TestOutcome outcome = abortOutcome.Value;
            if (ex == null && alreadyLoggedAbortOnce)
                return outcome;

            alreadyLoggedAbortOnce = true;
            LogMessage(markupDocumentWriter, actionDescription, outcome, abortMessage, ex);
            return outcome;
        }

        private static void LogMessage(MarkupDocumentWriter markupDocumentWriter, string actionDescription, TestOutcome outcome, string message, Exception ex)
        {
            if (string.IsNullOrEmpty(message) && ex == null)
                return;

            MarkupStreamWriter stream = GetLogStreamWriterForOutcome(markupDocumentWriter, outcome);
            using (actionDescription != null ? stream.BeginSection(actionDescription) : null)
            {
                if (! string.IsNullOrEmpty(message))
                    stream.WriteLine(message);

                if (ex != null)
                    stream.WriteException(StackTraceFilter.FilterException(ex));
            }
        }

        private static MarkupStreamWriter GetLogStreamWriterForOutcome(MarkupDocumentWriter markupDocumentWriter, TestOutcome outcome)
        {
            switch (outcome.Status)
            {
                case TestStatus.Passed:
                    return markupDocumentWriter.Default;
                case TestStatus.Failed:
                    return markupDocumentWriter.Failures;
                default:
                    return markupDocumentWriter.Warnings;
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException("Sandbox was disposed.");
        }
    }
}
