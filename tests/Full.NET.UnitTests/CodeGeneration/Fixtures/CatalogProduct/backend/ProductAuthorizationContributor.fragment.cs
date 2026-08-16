// <fullnet-generated catalog.product permissions>
new PermissionDefinition(
    ProductPermissions.Read,
    "读取 Product",
    AuthorizationScope.Host),
new PermissionDefinition(
    ProductPermissions.Write,
    "写入 Product",
    AuthorizationScope.Host),
// </fullnet-generated catalog.product permissions>

// <fullnet-generated catalog.product navigation>
new NavigationDefinition(
    "products",
    null,
    "products",
    "/catalog/products",
    "products",
    "Product",
    "Product",
    "collection",
    80,
    ProductPermissions.Read),
// </fullnet-generated catalog.product navigation>

// <fullnet-generated catalog.product actions>
new AuthorizationActionDefinition(
    "catalog.products.write",
    "products",
    ProductPermissions.Write,
    "写入",
    "write",
    10),
// </fullnet-generated catalog.product actions>
