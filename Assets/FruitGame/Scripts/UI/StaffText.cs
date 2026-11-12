using System.Collections.Generic;

namespace FruitGame
{
    /// <summary>
    /// The staff panel's own language (Docs/DESIGN.md §15), separate from the guests' language: staff can read the
    /// panel in Korean while the game runs in English, or the reverse. Text is written in English in the code and
    /// looked up here; anything missing from the table stays English. Short noun phrases, so they fit the rows.
    /// The debug readouts (camera, tracking rates) stay English.
    /// </summary>
    public static class StaffText
    {
        public static bool Korean;

        public static string T(string en) => Korean && en != null && Ko.TryGetValue(en, out var k) ? k : en;
        public static string F(string en, params object[] args) => string.Format(T(en), args);

        static readonly Dictionary<string, string> Ko = new Dictionary<string, string>
        {
            // Panel frame
            ["Staff panel"] = "진행자 패널",
            ["keys on"] = "키 켜짐",
            ["keys off"] = "키 꺼짐",
            ["` or F1 or Esc: off"] = "` · F1 · Esc: 끄기",
            ["` or F1: on"] = "` · F1: 켜기",
            ["` or F1 or Esc: close"] = "` · F1 · Esc: 닫기",
            ["Status"] = "상태",
            ["Gameplay"] = "게임",
            ["Camera"] = "카메라",
            ["Display"] = "화면",
            ["Printing"] = "인쇄",
            ["Beauty"] = "뷰티",
            ["Click again to reset every setting"] = "한 번 더 누르면 모든 설정 초기화",
            ["Standby: open the booth"] = "대기 중: 부스 열기",
            ["Standby"] = "대기 화면",
            ["Opening..."] = "여는 중...",
            ["Closing..."] = "닫는 중...",
            ["Reset settings to defaults"] = "설정을 기본값으로 초기화",

            // Common values
            ["On"] = "켜짐",
            ["Off"] = "꺼짐",
            ["none"] = "없음",
            ["Auto"] = "자동",
            ["{0} s"] = "{0}초",

            // Gameplay
            ["Mode each visit starts in"] = "방문마다 시작 모드",
            ["Normal"] = "보통",
            ["Easy"] = "쉬움",
            ["Sound"] = "소리",
            ["Hard"] = "어려움",
            ["Assist"] = "보조",
            ["Low"] = "약하게",
            ["Steering picked at the start"] = "처음에 고른 조작",
            ["Turning"] = "고개 돌리기",
            ["Sliding"] = "옆으로 움직이기",
            ["Head sensitivity"] = "머리 감도",
            ["Calibration: how still"] = "보정: 멈춤 허용 범위",
            ["Calibration: hold time"] = "보정: 멈춤 시간",
            ["Round length (next round)"] = "한 판 길이(다음 판)",
            ["Easy for the next round"] = "다음 판 쉽게",
            ["Recalibrate"] = "다시 보정",
            ["on the title screen"] = "시작 화면에서만",
            ["Visitor's call for help (0)"] = "손님 도움 요청(0)",
            ["waiting {0}, clear"] = "{0} 기다림, 지우기",
            ["Today's best score"] = "오늘 최고 점수",
            ["press again to reset"] = "한 번 더 누르면 초기화",
            ["{0}, reset"] = "{0}, 초기화",
            ["Basket follows the mouse"] = "마우스로 바구니 움직이기",

            // Camera
            ["Change camera"] = "카메라 바꾸기",
            ["Off (plain background)"] = "꺼짐(단색 배경)",
            ["Mirror the camera"] = "카메라 좌우 반전",
            ["Background removal"] = "배경 제거",
            ["Rotation"] = "회전",
            ["not built yet"] = "아직 없음",
            ["Face box on the guest screen"] = "손님 화면에 얼굴 상자",

            // Display
            ["Orientation"] = "화면 방향",
            ["Portrait"] = "세로",
            ["Landscape"] = "가로",
            ["Game language"] = "게임 언어",
            ["Staff panel language"] = "진행자 패널 언어",
            ["Screens (after restart)"] = "화면 수(다시 켠 뒤)",
            ["Main display shows (after restart)"] = "주 디스플레이에 띄울 화면(다시 켠 뒤)",
            ["Staff screen"] = "진행자 화면",
            ["Visitor has no keypad (voice assist)"] = "방문객에게 키패드 없음(음성 안내)",
            ["no keypad: staff answer"] = "키패드 없음: 직원이 대신 누름",
            ["has keypad"] = "키패드 있음",
            ["Guest screen"] = "손님 화면",
            ["One"] = "하나",
            ["Two"] = "둘",
            ["Blank the guest screen"] = "손님 화면 가리기",
            ["Lighting"] = "조명",
            ["Classic"] = "기본",
            ["Realistic"] = "사실적",
            ["Ray tracing (Realistic)"] = "레이 트레이싱(사실적)",

            // Printing
            ["Copies per print"] = "한 번에 인쇄 매수",
            ["Printer"] = "프린터",
            ["Default"] = "기본",
            ["Phone link"] = "휴대폰 링크",
            ["Local Wi-Fi"] = "로컬 Wi-Fi",
            ["Internet"] = "인터넷",
            ["up"] = "연결됨",
            ["starting…"] = "시작 중…",
            ["connecting…"] = "연결 중…",
            ["checking…"] = "확인 중…",
            ["no answer (no internet?)"] = "응답 없음(인터넷?)",
            ["cloudflared not found"] = "cloudflared 없음",
            ["stopped, restarting…"] = "멈춤, 다시 켜는 중…",
            ["photo server down"] = "사진 서버 꺼짐",
            ["Printer: "] = "프린터: ",
            ["No printer set up"] = "프린터가 설정되지 않음",
            ["printing"] = "인쇄 중",
            ["idle"] = "대기",
            ["stopped (check System Settings)"] = "멈춤(시스템 설정 확인)",
            ["paper and ink OK"] = "용지·잉크 정상",
            ["Paper and ink: no warnings"] = "용지·잉크: 경고 없음",
            ["Queued: {0}"] = "대기열: {0}",
            ["Printed so far: {0}"] = "지금까지 인쇄: {0}",
            ["PAPER OUT: refill the tray"] = "용지 없음: 용지함 채우기",
            ["paper low"] = "용지 부족",
            ["PAPER JAM"] = "용지 걸림",
            ["PAPER TRAY MISSING"] = "용지함 없음",
            ["INK OUT: change the cassette"] = "잉크 없음: 카세트 교체",
            ["ink low"] = "잉크 부족",
            ["COVER OPEN"] = "덮개 열림",
            ["printer offline (power or USB-C?)"] = "연결 끊김(전원·USB-C 확인)",
            ["(F1 staff panel)"] = "(F1 진행자 패널)",

            // Beauty
            ["Skin smoothing"] = "피부 보정",
            ["Smoothing width"] = "보정 폭",
            ["Blush"] = "블러셔",
            ["Blush tone"] = "블러셔 톤",
            ["Lip tint"] = "립 틴트",
            ["Lip tone"] = "립 톤",
            ["{0} (base)"] = "{0} (기본)",
            ["{0} warmer"] = "{0} 따뜻하게",
            ["{0} cooler"] = "{0} 차갑게",

            // Performance meter (staff screen)
            ["Performance"] = "성능",
            ["Whole frame"] = "프레임 전체",
            ["CPU (main thread)"] = "CPU (메인 스레드)",
            ["Face finding"] = "얼굴 찾기",
            ["Face mesh"] = "얼굴 메시",
            ["Cut-out & beauty"] = "컷아웃 · 뷰티",
            ["Mattes"] = "매트",
            ["Face"] = "얼굴",
            ["Mesh"] = "메시",
            ["spread"] = "나눠 돌림",
            ["1% low"] = "하위 1%",
            ["Rest of the game"] = "게임의 나머지",
            ["Waiting for the GPU"] = "GPU 기다림",
            ["Frame cost"] = "프레임 비용",
            ["last minute"] = "최근 1분",
            ["this app"] = "이 앱",
            ["temperatures need macmon (brew install macmon)"] = "온도는 macmon 필요(brew install macmon)",
        };
    }
}
