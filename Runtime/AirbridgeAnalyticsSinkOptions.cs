using System;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace

namespace Geuneda.Analytics.Airbridge
{
	/// <summary><see cref="AirbridgeAnalyticsSink"/> 설정입니다. 사전은 sink 를 만들 때 복사합니다.</summary>
	public sealed class AirbridgeAnalyticsSinkOptions
	{
		/// <summary>
		/// 게임이 이벤트를 직접 보낼 때 씁니다. true 를 돌려주면 이 sink 는 그 이벤트를 보내지 않습니다. 메인 스레드에서 이벤트마다 불립니다.
		/// </summary>
		public Func<AnalyticsEvent, bool> HandleEvent { get; set; }

		/// <summary>이벤트 이름 → Airbridge 카테고리(예: <c>tutorial_complete</c> → <c>AirbridgeCategory.COMPLETE_TUTORIAL</c>). 없으면 이벤트 이름을 그대로 씁니다.</summary>
		public IDictionary<string, string> Categories { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>
		/// 파라미터 이름 → Airbridge 시맨틱 속성 키. 여기에 있는 파라미터는 시맨틱 속성(리포트에서 나눠 볼 수 있음), 나머지는 커스텀 속성으로 보냅니다.
		/// 기본은 <c>action</c>, <c>label</c>, <c>value</c>, <c>currency</c> 입니다.
		/// </summary>
		public IDictionary<string, string> SemanticAttributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			{ "action", AirbridgeAttribute.ACTION },
			{ "label", AirbridgeAttribute.LABEL },
			{ "value", AirbridgeAttribute.VALUE },
			{ "currency", AirbridgeAttribute.CURRENCY }
		};
	}
}
