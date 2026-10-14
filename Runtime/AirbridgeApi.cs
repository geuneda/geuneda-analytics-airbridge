using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Airbridge
{
	/// <summary>Airbridge SDK 호출입니다. 테스트는 기록하는 구현으로 바꿉니다.</summary>
	internal interface IAirbridgeApi
	{
		void TrackEvent(string category, Dictionary<string, object> semanticAttributes, Dictionary<string, object> customAttributes);

		/// <summary>null 이면 사용자 ID 를 지웁니다.</summary>
		void SetUserId(string userId);

		/// <summary>null 이면 속성을 지웁니다.</summary>
		void SetUserAttribute(string key, string value);
	}

	/// <summary>
	/// 실제 Airbridge SDK(전역 <c>Airbridge</c> 클래스)를 부릅니다. 이 어셈블리의 네임스페이스 <c>Geuneda.Analytics.Airbridge</c> 가 이름을 가리므로 <c>global::</c> 로 부릅니다.
	/// </summary>
	internal sealed class AirbridgeApi : IAirbridgeApi
	{
		public void TrackEvent(string category, Dictionary<string, object> semanticAttributes, Dictionary<string, object> customAttributes)
		{
			global::Airbridge.TrackEvent(category, semanticAttributes, customAttributes);
		}

		public void SetUserId(string userId)
		{
			if (userId == null)
			{
				global::Airbridge.ClearUserID();
			}
			else
			{
				global::Airbridge.SetUserID(userId);
			}
		}

		public void SetUserAttribute(string key, string value)
		{
			if (value == null)
			{
				global::Airbridge.RemoveUserAttribute(key);
			}
			else
			{
				global::Airbridge.SetUserAttribute(key, value);
			}
		}
	}
}
