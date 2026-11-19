import copy
from django.test import SimpleTestCase
from dashboard.plugin_import_verification import imported_snapshot, EDM_BUILD, EDM_MARKER, EDM_MARKER_SHA, POLICY


class ImportVerificationTests(SimpleTestCase):
    def setUp(self):
        self.target=dict(operation='plugin',format='unitypackage',build_id=EDM_BUILD)
        self.expected=[dict(path=p,folder=folder,generated=folder,sha256='' if folder else sha,meta_sha256='a'*64,guid='b'*32)
            for p,folder,sha in [('Assets/ExternalDependencyManager/Editor/Google.VersionHandler.dll',False,'c'*64),
                ('Assets/PlayServicesResolver',True,''),('Assets/PlayServicesResolver/Editor',True,''),(EDM_MARKER,False,EDM_MARKER_SHA)]]
        self.omitted=[dict(f,existed=False,meta_existed=False,before_sha256='',before_meta_sha256='') for f in self.expected[1:]]
        self.record=dict(verification=POLICY,omitted_files=self.omitted)

    def test_exact_edm_build_only_omits_known_old_marker_and_empty_generated_parents(self):
        enabled,files=imported_snapshot(self.target,self.record,self.expected)
        self.assertTrue(enabled);self.assertEqual(files,self.expected[:1])
        for change in (dict(build_id='0'*64),dict(format='zip'),dict(operation='plugin_update')):
            with self.assertRaises(ValueError):imported_snapshot(dict(self.target,**change),self.record,self.expected)

    def test_omissions_cannot_hide_existing_files_required_dlls_or_nonempty_parents(self):
        for field,value in [('existed',True),('meta_existed',True),('before_meta_sha256','a'*64),('sha256','0'*64)]:
            wrong=copy.deepcopy(self.record);wrong['omitted_files'][-1][field]=value
            with self.assertRaises(ValueError):imported_snapshot(self.target,wrong,self.expected)
        for omitted in [self.omitted[:-1],self.omitted[:1]*2,[dict(self.expected[0],existed=False,meta_existed=False,before_sha256='',before_meta_sha256='')]]:
            with self.assertRaises(ValueError):imported_snapshot(self.target,dict(verification=POLICY,omitted_files=omitted),self.expected)

    def test_legacy_record_still_requires_all_archive_files(self):
        self.assertEqual(imported_snapshot(self.target,{},self.expected),(False,self.expected))
