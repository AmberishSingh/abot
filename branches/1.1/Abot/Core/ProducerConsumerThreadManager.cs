﻿using log4net;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Abot.Core
{
    public interface IThreadManager
    {
        /// <summary>
        /// Max number of threads to use. Note: if calling DoWork(Action, int) the actual number of threads used maybe up to two times this value.
        /// </summary>
        int MaxThreads { get; }

        /// <summary>
        /// Will perform the action asynchrously on a seperate thread
        /// </summary>
        /// <param name="action">The action to perform</param>
        void DoWork(Action action);

        /// <summary>
        /// Whether there are running threads
        /// </summary>
        bool HasRunningThreads();

        /// <summary>
        /// Abort all running threads
        /// </summary>
        void AbortAll();
    }

    public class ProducerConsumerThreadManager : IThreadManager
    {
        static ILog _logger = LogManager.GetLogger(typeof(ProducerConsumerThreadManager).FullName);

        CancellationTokenSource[] _consumerThreadCancellationTokens;
        BlockingCollection<Action> _actionsToExecute = new BlockingCollection<Action>();
        ConcurrentStack<int> _inProcessActionsToExecute = new ConcurrentStack<int>();

        public ProducerConsumerThreadManager(int maxThreads)
        {
            if ((maxThreads > 100) || (maxThreads < 1))
                throw new ArgumentException("MaxThreads must be from 1 to 100");
            
            _consumerThreadCancellationTokens = new CancellationTokenSource[maxThreads];

            if (maxThreads > 1)
            {
                for (int i = 0; i < maxThreads; i++)
                {
                    _consumerThreadCancellationTokens[i] = new CancellationTokenSource();
                    Task.Factory.StartNew(() => RunConsumer(i), _consumerThreadCancellationTokens[i].Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
            }
        }

        /// <summary>
        /// Max number of threads to use
        /// </summary>
        public int MaxThreads
        {
            get
            {
                return _consumerThreadCancellationTokens.Length;
            }
        }

        /// <summary>
        /// Will perform the action asynchrously on a seperate thread
        /// </summary>
        public void DoWork(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");

            if (MaxThreads > 1)
                _actionsToExecute.Add(action);
            else
                action.Invoke();
        }


        /// <summary>
        /// Whether there are running threads
        /// </summary>
        public bool HasRunningThreads()
        {
            return _inProcessActionsToExecute.Count > 0;
        }

        /// <summary>
        /// Abort all running threads
        /// </summary>
        public void AbortAll()
        {
            _inProcessActionsToExecute.Clear();

            foreach (CancellationTokenSource cancellationTokenSource in _consumerThreadCancellationTokens)
            {
                if(cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
                    cancellationTokenSource.Cancel();
            }

            _actionsToExecute.CompleteAdding();
            _inProcessActionsToExecute.Clear();
        }

        private void RunConsumer(int i)
        {
            foreach (Action action in _actionsToExecute.GetConsumingEnumerable())
            {
                ReportAsInProgress(action);
                try
                {
                    action.Invoke();
                }
                finally
                {
                    ReportAsProgressComplete(action);
                }
            }
        }

        /// <summary>
        /// Using a stack to keep track of in process actions. If _inprocessActions > 0 then we know there is a running thread
        /// </summary>
        private void ReportAsInProgress(Action action)
        {
            _inProcessActionsToExecute.Push(1);
        }

        /// <summary>
        /// Using a stack to keep track of in process actions. If _inprocessActions > 0 then we know there is a running thread
        /// </summary>
        private void ReportAsProgressComplete(Action action)
        {
            int val;
            _inProcessActionsToExecute.TryPop(out val);
        }
    }

    internal class ConsumerAction
    {
        public Action Action { get; set; }
        public int TimeoutInMillisecs { get; set; }
    }
}