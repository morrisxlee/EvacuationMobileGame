using UnityEngine;

namespace AllIn1SpriteShader
{
	public static class AllIn1InputSystem
	{
		public static bool GetKeyDown(KeyCode keyCode)
		{
			return Input.GetKeyDown(keyCode);
		}

		public static bool GetKey(KeyCode keyCode)
		{
			return Input.GetKey(keyCode);
		}

		public static float GetMouseXAxis()
		{
			return Input.GetAxis("Mouse X");
		}

		public static float GetMouseYAxis()
		{
			return Input.GetAxis("Mouse Y");
		}

		public static float GetMouseScroll()
		{
			return Input.GetAxis("Mouse ScrollWheel");
		}
	}
}
