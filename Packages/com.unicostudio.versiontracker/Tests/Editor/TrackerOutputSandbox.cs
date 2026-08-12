using System.IO;
using UnityEngine;

namespace UnicoStudio.UnicoLibs.VersionTracker.Tests
{
    /// <summary>
    /// Moves an existing UnicoVersionTracker/ output folder (dev-machine smoke-build
    /// residue) aside for the duration of a fixture and restores it afterwards, so
    /// tests can assert on a folder that starts absent without destroying anything.
    /// Tracker output is <c>&lt;project&gt;/../UnicoVersionTracker</c> resolved from
    /// Assets — the project root.
    /// </summary>
    internal sealed class TrackerOutputSandbox
    {
        private readonly string _dir;
        private readonly string _aside;
        private bool _movedAside;

        public string Dir => _dir;

        public TrackerOutputSandbox()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _dir = Path.Combine(projectRoot, "UnicoVersionTracker");
            _aside = _dir + ".aside-for-tests";
        }

        public void Enter()
        {
            // A leftover .aside means a previous run was killed mid-fixture (TearDown
            // never ran). Self-heal: the .aside holds the ORIGINAL user data, anything
            // at the real path is that run's test residue — drop the residue, restore
            // the original, then proceed normally. A throw here would instead poison
            // every later run until someone cleans up by hand.
            if (Directory.Exists(_aside))
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
                Directory.Move(_aside, _dir);
            }
            if (Directory.Exists(_dir))
            {
                Directory.Move(_dir, _aside);
                _movedAside = true;
            }
        }

        public void Exit()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            if (_movedAside && Directory.Exists(_aside)) Directory.Move(_aside, _dir);
            _movedAside = false;
        }
    }
}
