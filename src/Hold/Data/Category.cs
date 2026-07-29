namespace Hold.Data;

// One category per item. Not a many-to-many, not free-text tags.
// Stored as its name, so renaming a member orphans existing rows.
public enum Category
{
    Tops,
    Bottoms,
    Dresses,
    Outerwear,
    Shoes,
    Bags,
    Jewellery,
    Beauty,
    Home,
    Tech,
    Other,
}
