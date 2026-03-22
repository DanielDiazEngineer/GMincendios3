/************************************************************************************
Copyright : Copyright (c) Facebook Technologies, LLC and its affiliates. All rights reserved.

Your use of this SDK or tool is subject to the Oculus SDK License Agreement, available at
https://developer.oculus.com/licenses/oculussdk/

Unless required by applicable law or agreed to in writing, the Utilities SDK distributed
under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
ANY KIND, either express or implied. See the License for the specific language governing
permissions and limitations under the License.
************************************************************************************/

using UnityEngine;

namespace Oculus.Interaction.Samples
{
    public class StayInView : MonoBehaviour
    {
        [SerializeField]
        private Transform _eyeCenter;

        [SerializeField]
        private float _extraDistanceForward = 0;

        [SerializeField]
        private bool _zeroOutEyeHeight = true;

        Vector3 currentPos;
        public float lerpspeed = 5;
        /// <summary>
        /// Start is called on the frame when a script is enabled just before
        /// any of the Update methods is called the first time.
        /// </summary>
        void Start()
        {
            currentPos = transform.position;
        }
        void FixedUpdate()
        {
            transform.rotation = Quaternion.identity;
            transform.position = _eyeCenter.position;


            transform.Rotate(0, _eyeCenter.rotation.eulerAngles.y, 0, Space.Self);

            // transform.position = _eyeCenter.position + transform.forward.normalized * _extraDistanceForward;
            transform.position = Vector3.Lerp(currentPos, _eyeCenter.position + transform.forward.normalized * _extraDistanceForward, Time.deltaTime * lerpspeed);
            currentPos = transform.position;

            if (_zeroOutEyeHeight)
            {
                transform.position = new Vector3(transform.position.x, 0, transform.position.z);

            }
        }
    }
}
