import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('checker', Path(__file__).resolve().parents[1] / 'check-localization.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class LocalizationCheckerTests(unittest.TestCase):
    def test_conditions_options_and_formatted_values(self):
        source = 'Locale.T(done ? "완료" : "진행 중"); Locale.Options("원소", "등급"); Locale.Format("{0:N0}개", number, "사용자 데이터");'
        self.assertEqual(set(checker.declared_strings(source)), {'완료', '진행 중', '원소', '등급', '{0:N0}개'})

    def test_comments_and_nested_commas(self):
        source = '// Locale.T("주석")\nLocale.Format(ready ? "완료 {0}" : "대기 {0}", Build(1, 2)); /* Locale.T("주석") */'
        self.assertEqual(set(checker.declared_strings(source)), {'완료 {0}', '대기 {0}'})

    def test_direct_ui_bypass(self):
        source = 'PanelUi.Button(ready ? "시작" : "종료"); PanelUi.Text(Locale.T("원소")); PanelUi.Text(userText);'
        self.assertEqual(list(checker.untranslated_ui_literals(source)), ['시작', '종료'])


if __name__ == '__main__':
    unittest.main()
