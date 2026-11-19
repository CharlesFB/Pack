import re
from urllib.parse import urlsplit
from django import forms
from django.contrib.auth.decorators import user_passes_test
from django.shortcuts import render, redirect
from django.views.decorators.debug import sensitive_post_parameters
from django.views.decorators.cache import never_cache
from .models import GitLabSettings
from . import catalog


class GitLabSettingsForm(forms.Form):
    url = forms.URLField(label='GitLab 地址', max_length=200)
    ref = forms.CharField(label='目录分支 / Ref', max_length=256)
    private_token = forms.CharField(label='Private token', required=False, max_length=4096,
        strip=True, widget=forms.PasswordInput(render_value=False, attrs={'autocomplete':'new-password'}),
        help_text='留空保留当前 token；页面不会显示已保存的 token。')
    blacklist = forms.CharField(label='黑名单工程 ID', required=False, widget=forms.Textarea(attrs={'rows':6}),
        help_text='填写 GitLab 工程 ID，以逗号、空格或换行分隔。已安装包仍会显示，依赖树保持完整。')
    debug_mode = forms.BooleanField(label='Debug 模式：显示黑名单中的全部包', required=False)

    def clean_url(
        self,  # 当前对象实例
    ):
        """
        校验 GitLab HTTPS 地址并统一尾部斜杠，拒绝嵌入凭据与查询参数。
        """
        url = self.cleaned_data['url']
        p = urlsplit(url)
        if p.scheme != 'https' or p.username or p.password or p.query or p.fragment:
            raise forms.ValidationError('请填写不含凭据、查询参数的 HTTPS GitLab 地址。')
        return url.rstrip('/') + '/'

    def clean_blacklist(
        self,  # 当前对象实例
    ):
        """
        将分隔输入解析为去重的正整数工程 ID 列表，并限制数量。
        """
        ids = [v for v in re.split(r'[\s,，;；]+', self.cleaned_data['blacklist']) if v]
        if len(ids) > 2000 or any(not v.isascii() or not v.isdigit() or int(v) <= 0 or len(v)>18 for v in ids):
            raise forms.ValidationError('工程 ID 必须是正整数，最多 2000 项。')
        return list(dict.fromkeys(str(int(v)) for v in ids))


@never_cache
@sensitive_post_parameters('private_token')
@user_passes_test(lambda u: u.is_active and u.is_staff, login_url='/accounts/login/')
def settings_page(
    request,  # Django 请求，包含登录账号和会话
):
    """
    呈现和保存管理员 GitLab 设置；token 留空时沿用已有凭据，不回显原文。
    """
    current = GitLabSettings.objects.filter(pk=1).first()
    try:
        _, url, token, ref = catalog.source()
    except (ValueError, OSError, KeyError):
        url, token, ref = 'https://gitlab.mte.io/', '', 'master'
    initial = dict(url=current.url if current else url, ref=current.ref if current else ref,
                   blacklist='\n'.join(current.blacklist if current else catalog.blacklist_settings()[0]),
                   debug_mode=current.debug_mode if current else False)
    form = GitLabSettingsForm(request.POST if request.method == 'POST' else None, initial=initial)
    if request.method == 'POST' and form.is_valid():
        values = form.cleaned_data.copy()
        values['private_token'] = values['private_token'] or token
        if not values['private_token']:
            form.add_error('private_token', '首次配置需要填写 token。')
        else:
            GitLabSettings.objects.update_or_create(pk=1, defaults=values)
            return redirect('/settings/?saved=1')
    return render(request,'dashboard/settings.html',{'form':form,'saved':request.GET.get('saved')=='1','token_configured':bool(token)})


class DeviceBindingForm(forms.Form):
    token = forms.CharField(label='本机设备 Token', max_length=256,
        widget=forms.PasswordInput(render_value=False, attrs={'autocomplete':'off'}))


@never_cache
@sensitive_post_parameters('token')
@user_passes_test(lambda u: u.is_active, login_url='/accounts/login/')
def device_page(
    request,  # Django 请求，包含登录账号和会话
):
    """
    将已登录账号的有效设备凭据绑定到浏览器会话，或解除当前绑定。
    """
    import hashlib
    from .models import BridgeCredential
    form = DeviceBindingForm(request.POST if request.method == 'POST' else None)
    if request.method == 'POST' and request.POST.get('action') == 'unbind':
        request.session.pop('bridge_device_id', None)
        return redirect('/accounts/device/')
    if request.method == 'POST' and form.is_valid():
        credential = BridgeCredential.objects.filter(owner=request.user, active=True,
            digest=hashlib.sha256(form.cleaned_data['token'].encode()).hexdigest()).first()
        if credential is None:
            form.add_error('token','设备 Token 无效、已撤销或不属于当前账号。')
        else:
            request.session.cycle_key()
            request.session['bridge_device_id'] = credential.pk
            return redirect('/')
    device = BridgeCredential.objects.filter(pk=request.session.get('bridge_device_id'),owner=request.user,active=True).first()
    return render(request,'dashboard/device.html',{'form':form,'device':device})
