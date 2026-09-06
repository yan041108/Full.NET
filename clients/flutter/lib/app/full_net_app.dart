import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import '../core/auth/identity_session.dart';
import '../core/config/app_config.dart';
import '../core/http/full_net_http_client.dart';
import '../design_system/full_net_theme.dart';
import '../features/auth/login_page.dart';
import '../features/workflow/workflow_todo_client.dart';
import '../features/workflow/workflow_todos_page.dart';
import '../l10n/app_localizations.dart';

/// Full.NET Flutter 应用壳层：密码登录后进入工作流待办审批。
class FullNetApp extends StatefulWidget {
  const FullNetApp({super.key});

  @override
  State<FullNetApp> createState() => _FullNetAppState();
}

class _FullNetAppState extends State<FullNetApp> {
  late final FullNetHttpClient _http;
  late final IdentitySession _session;
  late final WorkflowTodoClient _workflowClient;
  var _authenticated = false;

  @override
  void initState() {
    super.initState();
    _http = FullNetHttpClient(locale: AppConfig.preferredLocale);
    _session = IdentitySession(_http);
    _workflowClient = WorkflowTodoClient(_http);
  }

  @override
  void dispose() {
    _http.close();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
      theme: buildFullNetTheme(brightness: Brightness.light),
      darkTheme: buildFullNetTheme(brightness: Brightness.dark),
      locale: Locale.fromSubtags(
        languageCode: AppConfig.preferredLocale.split('-').first,
        countryCode: AppConfig.preferredLocale.split('-').length > 1
            ? AppConfig.preferredLocale.split('-')[1]
            : null,
      ),
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: _authenticated
          ? WorkflowTodosPage(
              session: _session,
              client: _workflowClient,
              onSignedOut: () => setState(() => _authenticated = false),
            )
          : LoginPage(
              session: _session,
              onSignedIn: () => setState(() => _authenticated = true),
            ),
    );
  }
}
