using Fragsurf.Shared;
using Fragsurf.Utility;
using UnityEngine;

namespace Fragsurf
{
    public class GameCamera : SingletonComponent<GameCamera>
    {

        private static Camera _camera;
        public static Camera Camera => GetCamera();

        private static Camera GetCamera()
        {
            if (!_camera)
            {
                _camera = GameObject.Instantiate(Resources.Load<GameObject>("GameCamera")).GetComponent<Camera>();
            }
            return _camera;
        }

    }
}

