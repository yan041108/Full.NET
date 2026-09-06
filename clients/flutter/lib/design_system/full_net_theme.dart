import 'package:flutter/material.dart';

import 'full_net_tokens.dart';

/// 构建 Full.NET Material 3 主题；业务页面只依赖 ThemeData，不直接散落色值。
ThemeData buildFullNetTheme({required Brightness brightness}) {
  final colorScheme = brightness == Brightness.dark
      ? const ColorScheme.dark(
          primary: FullNetTokens.accentBright,
          onPrimary: FullNetTokens.ink,
          surface: FullNetTokens.sidebar,
          onSurface: Colors.white,
          error: FullNetTokens.danger,
        )
      : const ColorScheme.light(
          primary: FullNetTokens.accent,
          onPrimary: Colors.white,
          surface: FullNetTokens.panel,
          onSurface: FullNetTokens.ink,
          error: FullNetTokens.danger,
        );

  return ThemeData(
    useMaterial3: true,
    colorScheme: colorScheme,
    scaffoldBackgroundColor:
        brightness == Brightness.dark ? FullNetTokens.ink : FullNetTokens.canvas,
    appBarTheme: AppBarTheme(
      backgroundColor: colorScheme.surface,
      foregroundColor: colorScheme.onSurface,
      elevation: 0,
    ),
    inputDecorationTheme: InputDecorationTheme(
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(FullNetTokens.radiusSm),
      ),
    ),
    cardTheme: CardTheme(
      color: colorScheme.surface,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(FullNetTokens.radiusMd),
        side: BorderSide(color: FullNetTokens.line),
      ),
    ),
  );
}
