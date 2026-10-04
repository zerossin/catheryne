using System.Collections.Generic;
internal static class StoryAlerts {
 static readonly Dictionary<string,string> labels=new Dictionary<string,string>{
  {"restart_requires_observation","재시작 후 새 화면 확인이 필요합니다."},{"resume_requires_observation","AI가 새 화면을 확인하면 이어갈 수 있습니다."},{"user_request","사용자 요청으로 조작권을 변경했습니다."},{"user_stop","사용자가 입력을 중지했습니다."},
  {"supervisor_overdue","AI 응답 기한이 지났습니다."},{"combat_manual","전투 직접 수행 설정으로 조작권을 넘겼습니다."},{"combat_failures","설정한 전투 실패 횟수에 도달했습니다."},{"no_progress_limit","진전이 없어 사용자 도움이 필요합니다."},
  {"input_error","게임 입력을 확인해 주세요."},{"direct_input_error","게임 입력을 확인해 주세요."},{"assistance_error","게임 입력을 확인해 주세요."},{"external_error","게임 입력을 확인해 주세요."},{"watchdog_error","게임 입력을 확인해 주세요."},
  {"urgent_thinking","현재 게임 화면을 확인해 주세요."},{"urgent_chunk_finished","현재 게임 화면을 확인해 주세요."},{"urgent_direct_input_finished","현재 게임 화면을 확인해 주세요."}
 };
 internal static string Text(string code){string label;return Locale.T(labels.TryGetValue(code,out label)?label:"현재 진행 상태를 확인해 주세요.");}
 internal static bool RequiresAttention(string code){return labels.ContainsKey(code)&&code!="restart_requires_observation"&&code!="resume_requires_observation"&&code!="user_request"&&code!="user_stop";}
}
