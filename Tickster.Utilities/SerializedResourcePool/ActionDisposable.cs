using System;
using System.Diagnostics.CodeAnalysis;

namespace Tickster.Utils
{
    /// <summary>
    /// Utility IDisposable class.
    /// Calls the provided action when disposed. See http://vijay.screamingpens.com/archive/2008/05/26/actiondisposable.aspx 
    /// for the original implementation.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes", Justification = "Makes no sense to compare ActionDisposable. They are meant to be thrown away.")]
    public sealed class ActionDisposable : IDisposable
    {
        private Action _disposingAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActionDisposable"/> class.
        /// </summary>
        /// <param name="disposingAction">The disposing action.</param>
        public ActionDisposable(Action disposingAction)
        {
            _disposingAction = disposingAction;
        }

        /// <summary>
        /// Disposes the struct, calling the action provided on initialization.
        /// </summary>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            if (_disposingAction != null)
            {
                _disposingAction();
                _disposingAction = null;
            }
        }
    }
}
