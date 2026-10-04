import asyncio
import json
import subprocess
import unittest
from unittest.mock import patch

import server


class DesktopFailureTests(unittest.TestCase):
    def invoke(self, stdout):
        response = subprocess.CompletedProcess([], 1, stdout, "private process diagnostics")
        with patch.object(server, "settings", return_value={}), patch.object(server.Path, "is_file", return_value=True), patch("subprocess.run", return_value=response):
            return server.desktop_tool("catheryne_query", {"section": "build_analysis", "query": "missing"})

    def test_preserves_public_domain_reason(self):
        payload = {"error": "CATHERYNE_TOOL_FAILED", "message": "저장된 캐릭터를 찾을 수 없습니다."}
        self.assertEqual(self.invoke("\ufeff" + json.dumps(payload)), payload)

    def test_non_contract_output_is_not_exposed(self):
        for output in ("native crash details", "[]", '{"error":"PRIVATE","message":"diagnostics"}', '{"error":"CATHERYNE_TOOL_FAILED","message":{}}'):
            with self.subTest(output=output):
                self.assertEqual(self.invoke(output), {"error": "CATHERYNE_TOOL_FAILED"})


class CanonicalSchemaTests(unittest.TestCase):
    def test_all_canonical_tools_fields_enums_and_descriptions_are_published(self):
        published = {tool.name: tool for tool in asyncio.run(server.mcp.list_tools())}
        self.assertTrue(server.canonical_definitions, 'Desktop definitions unavailable')
        for name, definition in server.canonical_definitions.items():
            with self.subTest(tool=name):
                self.assertIn(name, published)
                self.assertEqual(published[name].description, definition['description'])
                expected = definition['inputSchema']['properties']
                actual = published[name].inputSchema['properties']
                self.assertEqual(set(actual), set(expected))
                for field, schema in expected.items():
                    if 'enum' in schema:
                        self.assertEqual(actual[field]['enum'], schema['enum'])

    def test_theater_conditional_contract_is_shared_verbatim(self):
        published={tool.name:tool for tool in asyncio.run(server.mcp.list_tools())}
        self.assertEqual(published['catheryne_theater'].inputSchema,server.canonical_definitions['catheryne_theater']['inputSchema'])

    def test_progress_passes_through_without_completing_task(self):
        args={'task_id':'synthetic','percent':40,'evidence':'Observed milestone'}
        with patch.object(server, 'desktop_tool', return_value={'state':'running'}) as call:
            asyncio.run(server.mcp.call_tool('catheryne_progress', args))
            call.assert_called_once_with('catheryne_progress', args)

    def test_goal_targets_are_preserved(self):
        args={'title':'Synthetic goal','category':'character','characterKey':'Amber','targetLevel':80,'targetTalent':8}
        with patch.object(server, 'desktop_tool', return_value={}) as call:
            asyncio.run(server.mcp.call_tool('catheryne_goal_add', args))
            call.assert_called_once_with('catheryne_goal_add', args)

    def test_interrupt_passes_transport_validation_without_game_input(self):
        with patch.object(server, 'desktop_tool', return_value={'owner': None}) as call:
            result = asyncio.run(server.mcp.call_tool('catheryne_game', {'command':'interrupt', 'parameters':{'task_id':'synthetic','evidence':'replan'}}))
            call.assert_called_once_with('catheryne_game', {'command':'interrupt', 'parameters':{'task_id':'synthetic','evidence':'replan'}})

    def test_route_query_passes_transport_validation(self):
        with patch.object(server, 'desktop_tool', return_value={'items':[]}) as call:
            asyncio.run(server.mcp.call_tool('catheryne_query', {'section':'bettergi_route', 'offset':0, 'query':'synthetic'}))
            call.assert_called_once_with('catheryne_query', {'section':'bettergi_route', 'offset':0, 'query':'synthetic'})


if __name__ == "__main__":
    unittest.main()
