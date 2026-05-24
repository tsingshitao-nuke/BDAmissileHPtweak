# BDAmissileHPtweak
a small mod that can automatically generate MM patches for parts that have [@MODULE[MissileLauncher]], and tweak their HP and armor based on mass.

The default setting is as follows:(you can change them in setting.cfg)

MissileHPSettings
{
    hpPerMass = 100
    massThreshold = 0.25
    minHP = 5
    
    armorPerMass = 20
    armorMassThreshold = 1
    minArmor = 2
}

This means that missiles lighter than 0.25 t will keep BDA's original HP, while missiles heavier than 0.25 t will gain an extra 100 HP per ton.
The same logic applies to armor.

dependency:
BDArmory
module manager

