using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using UnhollowerRuntimeLib;
using HarmonyLib;
using System.Reflection;

namespace VRCBhapticsIntegration
{
    internal class VRCBhapticsIntegration : MelonMod
	{
		private static Dictionary<bHaptics.PositionType, CameraParser> CameraParsers;
		private static int LayerForCulling;
		private static bool HasSteamTracking = false;
		private static bool HasOculusRiftTracking = false;
		private static bool HasOculusTouchTracking = false;
		internal static MelonLogger.Instance Logger;

		public override void OnApplicationStart()
        {
			if (bHaptics.WasError)
				return;

			Logger = LoggerInstance;

			LayerForCulling = LayerMask.NameToLayer("PlayerLocal");
			CameraParsers = new Dictionary<bHaptics.PositionType, CameraParser>();

			HasSteamTracking = typeof(VRCAvatarManager).Assembly.GetType("VRCTrackingSteam") != null;
			HasOculusRiftTracking = typeof(VRCAvatarManager).Assembly.GetType("VRCTrackingRift") != null;
			HasOculusTouchTracking = typeof(VRCAvatarManager).Assembly.GetType("VRCTrackingTouch") != null;

			ModConfig.Initialize();
			ClassInjector.RegisterTypeInIl2Cpp<CameraParser>();

			HarmonyInstance.Patch(typeof(VRCPlayer).GetMethod("Awake", BindingFlags.Public | BindingFlags.Instance), 
				null,
				new HarmonyMethod(typeof(VRCBhapticsIntegration).GetMethod("AwakePatch", BindingFlags.NonPublic | BindingFlags.Static)));
		}

		internal static void ResetCameraParser(bHaptics.PositionType pos)
        {
			if (!CameraParsers.TryGetValue(pos, out CameraParser parser)
				|| (parser == null))
				return;
			parser.OldColors = null;
		}

		// Credit to knah for this simple method to grab VRCTracking
		internal static T FindVRCTracking<T>() where T : VRCTracking
		{
			T VRCTrackingT = null;
			 foreach (VRCTracking vrcTracking in MonoBehaviourPublicObStBoTrLiObBoStTr1Unique.field_Private_Static_MonoBehaviourPublicObStBoTrLiObBoStTr1Unique_0.field_Private_List_1_VRCTracking_0)
			{
				VRCTrackingT = vrcTracking.TryCast<T>();
				if (VRCTrackingT != null)
					break;
			}
			return VRCTrackingT;
		}

		private static GameObject[] GatherTrackedObjects()
		{
			List<GameObject> objects = new List<GameObject>();

			if (HasSteamTracking)
			{
				GameObject[] steamvr = SteamTracking.GetTrackedObjects();
				if ((steamvr != null)
					&& (steamvr.Length > 0))
					objects.AddRange(steamvr);
			}

			if (HasOculusRiftTracking)
            {
				GameObject[] oculus_rift = OculusRiftTracking.GetTrackedObjects();
				if ((oculus_rift != null)
					&& (oculus_rift.Length > 0))
					objects.AddRange(oculus_rift);
			}

			if (HasOculusTouchTracking)
            {
				GameObject[] oculus_touch = OculusTouchTracking.GetTrackedObjects();
				if ((oculus_touch != null)
					&& (oculus_touch.Length > 0))
					objects.AddRange(oculus_touch);
			}

			return objects.ToArray();
		}

		private static void SetTrackedObjectsCullingLayer()
		{
			GameObject[] TrackedObjects = GatherTrackedObjects();
			if ((TrackedObjects != null)
				&& (TrackedObjects.Length > 0))
				foreach (GameObject obj in TrackedObjects)
				{
					if (obj == null)
						continue;
					SetLayerRecursive(obj, LayerForCulling);
					MelonDebug.Msg($"Added {obj.name} to Culling Layer");
				}
		}

		// Credit to loukylor for this Recursive Layer Set method
		private static void SetLayerRecursive(GameObject gameObject, int layer)
		{
			if (gameObject == null)
				return;
			gameObject.layer = layer;
			foreach (var child in gameObject.transform)
			{
				if (child == null)
					return;
				SetLayerRecursive(child.Cast<Transform>().gameObject, layer);
			}
		}

		// Credit to loukylor for this Avatar Instantiated Patch
		private static void AwakePatch(VRCPlayer __instance)
			=> __instance.Method_Public_add_Void_OnAvatarIsReady_0(new Action(()
				=> OnAvatarInstantiated(__instance.prop_VRCAvatarManager_0, __instance.field_Internal_GameObject_0))
			);

		private static void OnAvatarInstantiated(VRCAvatarManager avatar_manager, GameObject gameobject)
        {
			if (bHaptics.WasError)
				return;

			if ((avatar_manager == null)
				|| (gameobject == null))
				return;

			VRCPlayer vrcPlayer = VRCPlayer.field_Internal_Static_VRCPlayer_0;
			if (vrcPlayer == null)
				return;

			VRCAvatarManager vrcAvatarManager = vrcPlayer.prop_VRCAvatarManager_0;
			if ((vrcAvatarManager == null) || (vrcAvatarManager != avatar_manager))
				return;

			CameraParsers.Clear();

			Camera[] foundCameras = gameobject.GetComponentsInChildren<Camera>(true);
			if (foundCameras.Length <= 0)
				return;

			SetTrackedObjectsCullingLayer();

			for (int i = 0; i < foundCameras.Length; i++)
			{
				Camera cam = foundCameras[i];
				if (cam == null)
					continue;

				if (cam.gameObject.name.Contains("Dummy"))
					continue;

				RenderTexture tex = cam.targetTexture;
				if (tex == null)
					continue;

				if (!ModConfig.RenderTextureToPos.TryGetValue(tex.name, out bHaptics.PositionType pos))
					continue;

				CameraParser parser = cam.gameObject.AddComponent(Il2CppType.Of<CameraParser>()).TryCast<CameraParser>();
				parser.enabled = true;
				parser.Position = pos;

				parser._camera = cam;
				parser._camera.enabled = false;
				parser._camera.useOcclusionCulling = true;
				parser._camera.cullingMask &= ~(1 << LayerForCulling);

				parser.SetupFromConfig();
				CameraParsers[pos] = parser;

				VRCBhapticsIntegration.Logger.Msg(ModConfig.PosToName[pos] + " Linked!");
			}
		}
	}
}