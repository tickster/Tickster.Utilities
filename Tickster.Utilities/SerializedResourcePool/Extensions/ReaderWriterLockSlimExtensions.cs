using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Tickster.Utils;

namespace Tickster.Extensions
{
    public static class ReaderWriterLockSlimExtensions
    {
        public static IDisposable GetDisposableUpgradeableReadLock(this ReaderWriterLockSlim rwLock)
        {
            if (rwLock == null)
                throw new ArgumentNullException("rwLock");

            rwLock.EnterUpgradeableReadLock();

            return new ActionDisposable(rwLock.ExitUpgradeableReadLock);
        }

        public static IDisposable GetDisposableReadLock(this ReaderWriterLockSlim rwLock)
        {
            if (rwLock == null)
                throw new ArgumentNullException("rwLock");

            rwLock.EnterReadLock();

            return new ActionDisposable(rwLock.ExitReadLock);
        }

        public static IDisposable GetDisposableWriteLock(this ReaderWriterLockSlim rwLock)
        {
            if (rwLock == null)
                throw new ArgumentNullException("rwLock");

            rwLock.EnterWriteLock();

            return new ActionDisposable(rwLock.ExitWriteLock);
        }
    }
}
